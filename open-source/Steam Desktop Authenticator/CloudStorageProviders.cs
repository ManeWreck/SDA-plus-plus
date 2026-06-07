using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Identity.Client;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Xml.Linq;

namespace Steam_Desktop_Authenticator
{
    internal sealed class WebDavStorageProvider : ICloudStorageProvider, ICloudStorageFileListProvider
    {
        private readonly string url;
        private readonly string username;
        private readonly string password;
        private readonly string remotePath;

        public WebDavStorageProvider(string url, string username, string password, string remotePath)
        {
            this.url = url;
            this.username = username;
            this.password = password;
            this.remotePath = NormalizePath(remotePath);
        }

        public async Task<string> TestConnectionAsync()
        {
            using HttpClient client = CreateClient();
            using HttpRequestMessage request = new HttpRequestMessage(new HttpMethod("PROPFIND"), BuildDirectoryUri());
            request.Headers.Add("Depth", "0");
            request.Content = new StringContent(
                "<?xml version=\"1.0\" encoding=\"utf-8\" ?><d:propfind xmlns:d=\"DAV:\"><d:prop><d:displayname/></d:prop></d:propfind>",
                Encoding.UTF8,
                "application/xml");

            using HttpResponseMessage response = await client.SendAsync(request);
            if (!response.IsSuccessStatusCode && response.StatusCode != HttpStatusCode.MultiStatus)
            {
                throw new InvalidOperationException($"WebDAV connection failed: {(int)response.StatusCode} {response.ReasonPhrase}");
            }

            return "WebDAV connection successful.";
        }

        public async Task EnsureContainerAsync()
        {
            using HttpClient client = CreateClient();
            Uri baseUri = NormalizeBaseUri(url);
            string currentPath = string.Empty;
            foreach (string segment in remotePath.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries))
            {
                currentPath += Uri.EscapeDataString(segment) + "/";
                using HttpRequestMessage request = new HttpRequestMessage(new HttpMethod("MKCOL"), new Uri(baseUri, currentPath));
                using HttpResponseMessage response = await client.SendAsync(request);
                if (!response.IsSuccessStatusCode &&
                    response.StatusCode != HttpStatusCode.MethodNotAllowed &&
                    response.StatusCode != HttpStatusCode.Conflict)
                {
                    throw new InvalidOperationException($"Failed to create WebDAV folder: {(int)response.StatusCode} {response.ReasonPhrase}");
                }
            }
        }

        public async Task<string> DownloadTextAsync(string fileName, bool optional = false)
        {
            using HttpClient client = CreateClient();
            using HttpResponseMessage response = await client.GetAsync(BuildFileUri(fileName));
            if (optional && response.StatusCode == HttpStatusCode.NotFound)
            {
                return null;
            }

            await EnsureSuccessAsync(response, "download " + fileName);
            return await response.Content.ReadAsStringAsync();
        }

        public async Task UploadTextAsync(string fileName, string content)
        {
            using HttpClient client = CreateClient();
            using StringContent body = new StringContent(content, Encoding.UTF8, "application/json");
            using HttpResponseMessage response = await client.PutAsync(BuildFileUri(fileName), body);
            await EnsureSuccessAsync(response, "upload " + fileName);
        }

        public async Task<IReadOnlyCollection<string>> ListFileNamesAsync(string extensionFilter = null)
        {
            using HttpClient client = CreateClient();
            using HttpRequestMessage request = new HttpRequestMessage(new HttpMethod("PROPFIND"), BuildDirectoryUri());
            request.Headers.Add("Depth", "1");
            request.Content = new StringContent(
                "<?xml version=\"1.0\" encoding=\"utf-8\" ?><d:propfind xmlns:d=\"DAV:\"><d:prop><d:displayname/><d:resourcetype/></d:prop></d:propfind>",
                Encoding.UTF8,
                "application/xml");

            using HttpResponseMessage response = await client.SendAsync(request);
            if (!response.IsSuccessStatusCode && response.StatusCode != HttpStatusCode.MultiStatus)
            {
                await EnsureSuccessAsync(response, "list WebDAV directory");
            }

            string xml = await response.Content.ReadAsStringAsync();
            return ParsePropfindFileNames(xml, BuildDirectoryUri(), extensionFilter);
        }

        private HttpClient CreateClient()
        {
            Require(url, "WebDAV URL");
            Require(username, "WebDAV username");
            Require(password, "WebDAV password");

            HttpClient client = new HttpClient();
            string auth = Convert.ToBase64String(Encoding.UTF8.GetBytes(username + ":" + password));
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", auth);
            return client;
        }

        private Uri BuildDirectoryUri()
        {
            Uri baseUri = NormalizeBaseUri(url);
            return string.IsNullOrEmpty(remotePath) ? baseUri : new Uri(baseUri, EscapePath(remotePath) + "/");
        }

        private Uri BuildFileUri(string fileName)
        {
            return new Uri(BuildDirectoryUri(), Uri.EscapeDataString(fileName));
        }

        private static Uri NormalizeBaseUri(string value)
        {
            string normalized = (value ?? string.Empty).Trim();
            if (!normalized.EndsWith("/", StringComparison.Ordinal))
            {
                normalized += "/";
            }

            return new Uri(normalized, UriKind.Absolute);
        }

        private static string NormalizePath(string value) => (value ?? string.Empty).Trim().Trim('/');
        private static string EscapePath(string value) => string.Join("/", NormalizePath(value).Split('/').Select(Uri.EscapeDataString));

        private static IReadOnlyCollection<string> ParsePropfindFileNames(string xml, Uri directoryUri, string extensionFilter)
        {
            if (string.IsNullOrWhiteSpace(xml))
            {
                return Array.Empty<string>();
            }

            XNamespace dav = "DAV:";
            XDocument document = XDocument.Parse(xml);
            string directoryPath = TrimTrailingSlash(Uri.UnescapeDataString(directoryUri.AbsolutePath));
            HashSet<string> fileNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (XElement response in document.Descendants(dav + "response"))
            {
                string hrefValue = response.Element(dav + "href")?.Value;
                if (string.IsNullOrWhiteSpace(hrefValue) || !TryResolveHrefUri(directoryUri, hrefValue, out Uri hrefUri))
                {
                    continue;
                }

                bool isCollection = response.Descendants(dav + "collection").Any() || hrefValue.EndsWith("/", StringComparison.Ordinal);
                string path = TrimTrailingSlash(Uri.UnescapeDataString(hrefUri.AbsolutePath));
                if (string.Equals(path, directoryPath, StringComparison.OrdinalIgnoreCase) || isCollection)
                {
                    continue;
                }

                string fileName = Path.GetFileName(path);
                if (string.IsNullOrWhiteSpace(fileName))
                {
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(extensionFilter) && !fileName.EndsWith(extensionFilter, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                fileNames.Add(fileName);
            }

            return fileNames.OrderBy(name => name, StringComparer.OrdinalIgnoreCase).ToArray();
        }

        private static bool TryResolveHrefUri(Uri directoryUri, string hrefValue, out Uri hrefUri)
        {
            if (Uri.TryCreate(hrefValue, UriKind.Absolute, out hrefUri))
            {
                return true;
            }

            return Uri.TryCreate(directoryUri, hrefValue, out hrefUri);
        }

        private static string TrimTrailingSlash(string path)
        {
            return (path ?? string.Empty).TrimEnd('/');
        }

        private static async Task EnsureSuccessAsync(HttpResponseMessage response, string action)
        {
            if (!response.IsSuccessStatusCode)
            {
                string details = await response.Content.ReadAsStringAsync();
                throw new InvalidOperationException($"Failed to {action}: {(int)response.StatusCode} {response.ReasonPhrase}. {details}");
            }
        }

        private static void Require(string value, string name)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new InvalidOperationException(name + " is required.");
            }
        }
    }

    internal sealed class S3StorageProvider : ICloudStorageProvider, IDisposable
    {
        private readonly string bucket;
        private readonly string prefix;
        private readonly AmazonS3Client client;

        public S3StorageProvider(string endpoint, string bucket, string accessKey, string secretKey, string remotePath)
        {
            Require(endpoint, "S3 endpoint");
            Require(bucket, "S3 bucket");
            Require(accessKey, "S3 access key");
            Require(secretKey, "S3 secret key");

            this.bucket = bucket.Trim();
            prefix = NormalizePrefix(remotePath);
            AmazonS3Config config = new AmazonS3Config
            {
                ServiceURL = endpoint.Trim().TrimEnd('/'),
                ForcePathStyle = true,
                AuthenticationRegion = InferRegion(endpoint)
            };
            client = new AmazonS3Client(new BasicAWSCredentials(accessKey.Trim(), secretKey), config);
        }

        public async Task<string> TestConnectionAsync()
        {
            await client.ListObjectsV2Async(new ListObjectsV2Request
            {
                BucketName = bucket,
                Prefix = prefix,
                MaxKeys = 1
            });
            return "S3-compatible storage connection successful.";
        }

        public Task EnsureContainerAsync() => Task.CompletedTask;

        public async Task<string> DownloadTextAsync(string fileName, bool optional = false)
        {
            try
            {
                using GetObjectResponse response = await client.GetObjectAsync(bucket, BuildKey(fileName));
                using StreamReader reader = new StreamReader(response.ResponseStream, Encoding.UTF8);
                return await reader.ReadToEndAsync();
            }
            catch (AmazonS3Exception ex) when (optional && ex.StatusCode == HttpStatusCode.NotFound)
            {
                return null;
            }
        }

        public async Task UploadTextAsync(string fileName, string content)
        {
            await client.PutObjectAsync(new PutObjectRequest
            {
                BucketName = bucket,
                Key = BuildKey(fileName),
                ContentBody = content,
                ContentType = "application/json"
            });
        }

        public void Dispose() => client.Dispose();

        private string BuildKey(string fileName) => prefix + fileName;

        private static string NormalizePrefix(string value)
        {
            string normalized = (value ?? string.Empty).Trim().Trim('/');
            return string.IsNullOrEmpty(normalized) ? string.Empty : normalized + "/";
        }

        private static string InferRegion(string endpoint)
        {
            if (endpoint.IndexOf("r2.cloudflarestorage.com", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "auto";
            }

            Uri uri = new Uri(endpoint);
            string[] parts = uri.Host.Split('.');
            int s3Index = Array.FindIndex(parts, part => part.Equals("s3", StringComparison.OrdinalIgnoreCase));
            if (s3Index >= 0 && s3Index + 1 < parts.Length)
            {
                return parts[s3Index + 1];
            }

            return "us-east-1";
        }

        private static void Require(string value, string name)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new InvalidOperationException(name + " is required.");
            }
        }
    }

    internal sealed class DropboxStorageProvider : ICloudStorageProvider
    {
        private static readonly HttpClient Http = new HttpClient();
        private readonly string accessToken;
        private readonly string remotePath;

        public DropboxStorageProvider(string accessToken, string remotePath)
        {
            this.accessToken = accessToken;
            this.remotePath = NormalizePath(remotePath);
        }

        public async Task<string> TestConnectionAsync()
        {
            using HttpRequestMessage request = CreateRequest(HttpMethod.Post, "https://api.dropboxapi.com/2/users/get_current_account");
            request.Content = new StringContent("null", Encoding.UTF8, "application/json");
            using HttpResponseMessage response = await Http.SendAsync(request);
            await EnsureSuccessAsync(response, "connect to Dropbox");

            using HttpRequestMessage quotaRequest = CreateRequest(HttpMethod.Post, "https://api.dropboxapi.com/2/users/get_space_usage");
            quotaRequest.Content = new StringContent("null", Encoding.UTF8, "application/json");
            using HttpResponseMessage quotaResponse = await Http.SendAsync(quotaRequest);
            await EnsureSuccessAsync(quotaResponse, "read Dropbox storage usage");
            DropboxSpaceUsageResponse quota = JsonConvert.DeserializeObject<DropboxSpaceUsageResponse>(await quotaResponse.Content.ReadAsStringAsync());
            if (quota?.allocation?.allocated > 0)
            {
                return $"Dropbox connected. Used {CloudProviderFormatting.FormatBytes(quota.used)} of {CloudProviderFormatting.FormatBytes(quota.allocation.allocated)}.";
            }

            return "Dropbox connection successful.";
        }

        public async Task EnsureContainerAsync()
        {
            string current = string.Empty;
            foreach (string segment in remotePath.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries))
            {
                current += "/" + segment;
                using HttpRequestMessage request = CreateRequest(HttpMethod.Post, "https://api.dropboxapi.com/2/files/create_folder_v2");
                request.Content = JsonContent(new { path = current, autorename = false });
                using HttpResponseMessage response = await Http.SendAsync(request);
                if (response.IsSuccessStatusCode)
                {
                    continue;
                }

                string details = await response.Content.ReadAsStringAsync();
                if (!details.Contains("conflict", StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException($"Failed to create Dropbox folder: {(int)response.StatusCode}. {details}");
                }
            }
        }

        public async Task<string> DownloadTextAsync(string fileName, bool optional = false)
        {
            using HttpRequestMessage request = CreateRequest(HttpMethod.Post, "https://content.dropboxapi.com/2/files/download");
            request.Headers.Add("Dropbox-API-Arg", JsonConvert.SerializeObject(new { path = BuildPath(fileName) }));
            using HttpResponseMessage response = await Http.SendAsync(request);
            if (optional && response.StatusCode == HttpStatusCode.Conflict)
            {
                return null;
            }

            await EnsureSuccessAsync(response, "download " + fileName);
            return await response.Content.ReadAsStringAsync();
        }

        public async Task UploadTextAsync(string fileName, string content)
        {
            using HttpRequestMessage request = CreateRequest(HttpMethod.Post, "https://content.dropboxapi.com/2/files/upload");
            request.Headers.Add("Dropbox-API-Arg", JsonConvert.SerializeObject(new
            {
                path = BuildPath(fileName),
                mode = "overwrite",
                autorename = false,
                mute = true
            }));
            request.Content = new StringContent(content, Encoding.UTF8, "application/octet-stream");
            using HttpResponseMessage response = await Http.SendAsync(request);
            await EnsureSuccessAsync(response, "upload " + fileName);
        }

        private HttpRequestMessage CreateRequest(HttpMethod method, string url)
        {
            if (string.IsNullOrWhiteSpace(accessToken))
            {
                throw new InvalidOperationException("Dropbox access token is required.");
            }

            HttpRequestMessage request = new HttpRequestMessage(method, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            return request;
        }

        private string BuildPath(string fileName)
        {
            string root = string.IsNullOrEmpty(remotePath) ? string.Empty : "/" + remotePath;
            return root + "/" + fileName;
        }

        private static StringContent JsonContent(object value) =>
            new StringContent(JsonConvert.SerializeObject(value), Encoding.UTF8, "application/json");

        private static string NormalizePath(string value) => (value ?? string.Empty).Trim().Trim('/');

        private static async Task EnsureSuccessAsync(HttpResponseMessage response, string action)
        {
            if (!response.IsSuccessStatusCode)
            {
                string details = await response.Content.ReadAsStringAsync();
                throw new InvalidOperationException($"Failed to {action}: {(int)response.StatusCode} {response.ReasonPhrase}. {details}");
            }
        }
    }

    internal sealed class OneDriveStorageProvider : ICloudStorageProvider
    {
        private static readonly HttpClient Http = new HttpClient();
        private static readonly string[] Scopes = { "Files.ReadWrite.AppFolder" };
        private readonly string clientId;
        private readonly string remotePath;
        private readonly CloudSecretStore secretStore;
        private readonly IPublicClientApplication app;

        public OneDriveStorageProvider(string clientId, string remotePath, CloudSecretStore secretStore)
        {
            if (string.IsNullOrWhiteSpace(clientId))
            {
                throw new InvalidOperationException("OneDrive Client ID is required.");
            }

            this.clientId = clientId.Trim();
            this.remotePath = NormalizePath(remotePath);
            this.secretStore = secretStore;
            app = PublicClientApplicationBuilder.Create(this.clientId)
                .WithAuthority(AadAuthorityAudience.PersonalMicrosoftAccount)
                .WithDefaultRedirectUri()
                .Build();
            ConfigureTokenCache();
        }

        public async Task<string> TestConnectionAsync()
        {
            string token = await AcquireTokenAsync();
            using HttpRequestMessage request = CreateGraphRequest(HttpMethod.Get, "https://graph.microsoft.com/v1.0/me/drive?$select=quota", token);
            using HttpResponseMessage response = await Http.SendAsync(request);
            await EnsureSuccessAsync(response, "connect to OneDrive");

            OneDriveDriveResponse drive = JsonConvert.DeserializeObject<OneDriveDriveResponse>(await response.Content.ReadAsStringAsync());
            if (drive?.quota?.total > 0)
            {
                return $"OneDrive connected. Used {CloudProviderFormatting.FormatBytes(drive.quota.used)} of {CloudProviderFormatting.FormatBytes(drive.quota.total)}.";
            }

            return "OneDrive connection successful.";
        }

        public async Task EnsureContainerAsync()
        {
            if (string.IsNullOrEmpty(remotePath))
            {
                return;
            }

            string token = await AcquireTokenAsync();
            string parentPath = string.Empty;
            foreach (string segment in remotePath.Split('/'))
            {
                string itemPath = JoinPath(parentPath, segment);
                using HttpRequestMessage probe = CreateGraphRequest(
                    HttpMethod.Get,
                    "https://graph.microsoft.com/v1.0/me/drive/special/approot:/" + EscapePath(itemPath),
                    token);
                using HttpResponseMessage probeResponse = await Http.SendAsync(probe);
                if (probeResponse.IsSuccessStatusCode)
                {
                    parentPath = itemPath;
                    continue;
                }

                if (probeResponse.StatusCode != HttpStatusCode.NotFound)
                {
                    await EnsureSuccessAsync(probeResponse, "check OneDrive folder");
                }

                string childrenUrl = string.IsNullOrEmpty(parentPath)
                    ? "https://graph.microsoft.com/v1.0/me/drive/special/approot/children"
                    : "https://graph.microsoft.com/v1.0/me/drive/special/approot:/" + EscapePath(parentPath) + ":/children";
                using HttpRequestMessage create = CreateGraphRequest(HttpMethod.Post, childrenUrl, token);
                create.Content = JsonContent(new Dictionary<string, object>
                {
                    ["name"] = segment,
                    ["folder"] = new { },
                    ["@microsoft.graph.conflictBehavior"] = "fail"
                });
                using HttpResponseMessage createResponse = await Http.SendAsync(create);
                await EnsureSuccessAsync(createResponse, "create OneDrive folder");
                parentPath = itemPath;
            }
        }

        public async Task<string> DownloadTextAsync(string fileName, bool optional = false)
        {
            string token = await AcquireTokenAsync();
            using HttpRequestMessage request = CreateGraphRequest(HttpMethod.Get, BuildContentUrl(fileName), token);
            using HttpResponseMessage response = await Http.SendAsync(request);
            if (optional && response.StatusCode == HttpStatusCode.NotFound)
            {
                return null;
            }

            await EnsureSuccessAsync(response, "download " + fileName);
            return await response.Content.ReadAsStringAsync();
        }

        public async Task UploadTextAsync(string fileName, string content)
        {
            string token = await AcquireTokenAsync();
            using HttpRequestMessage request = CreateGraphRequest(HttpMethod.Put, BuildContentUrl(fileName), token);
            request.Content = new StringContent(content, Encoding.UTF8, "application/json");
            using HttpResponseMessage response = await Http.SendAsync(request);
            await EnsureSuccessAsync(response, "upload " + fileName);
        }

        public async Task SignInAsync()
        {
            IEnumerable<IAccount> accounts = await app.GetAccountsAsync();
            foreach (IAccount account in accounts)
            {
                await app.RemoveAsync(account);
            }

            await app.AcquireTokenWithDeviceCode(Scopes, ShowDeviceCodeAsync).ExecuteAsync();
        }

        private async Task<string> AcquireTokenAsync()
        {
            IAccount account = (await app.GetAccountsAsync()).FirstOrDefault();
            if (account != null)
            {
                try
                {
                    AuthenticationResult silent = await app.AcquireTokenSilent(Scopes, account).ExecuteAsync();
                    return silent.AccessToken;
                }
                catch (MsalUiRequiredException)
                {
                }
            }

            AuthenticationResult result = await app.AcquireTokenWithDeviceCode(Scopes, ShowDeviceCodeAsync).ExecuteAsync();
            return result.AccessToken;
        }

        private Task ShowDeviceCodeAsync(DeviceCodeResult result)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = result.VerificationUrl,
                    UseShellExecute = true
                });
            }
            catch
            {
            }

            MessageBox.Show(
                result.Message,
                Localizer.Choose("OneDrive sign in", "Вход в OneDrive"),
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return Task.CompletedTask;
        }

        private void ConfigureTokenCache()
        {
            app.UserTokenCache.SetBeforeAccess(args =>
            {
                byte[] cache = secretStore.LoadBytes("onedrive-token-cache-" + clientId);
                if (cache.Length > 0)
                {
                    args.TokenCache.DeserializeMsalV3(cache);
                }
            });
            app.UserTokenCache.SetAfterAccess(args =>
            {
                if (args.HasStateChanged)
                {
                    secretStore.SaveBytes("onedrive-token-cache-" + clientId, args.TokenCache.SerializeMsalV3());
                }
            });
        }

        private string BuildContentUrl(string fileName)
        {
            string path = JoinPath(remotePath, fileName);
            return "https://graph.microsoft.com/v1.0/me/drive/special/approot:/" + EscapePath(path) + ":/content";
        }

        private static HttpRequestMessage CreateGraphRequest(HttpMethod method, string url, string token)
        {
            HttpRequestMessage request = new HttpRequestMessage(method, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            return request;
        }

        private static string NormalizePath(string value) => (value ?? string.Empty).Trim().Trim('/');
        private static string JoinPath(string left, string right) =>
            string.IsNullOrEmpty(left) ? NormalizePath(right) : NormalizePath(left) + "/" + NormalizePath(right);
        private static string EscapePath(string value) =>
            string.Join("/", NormalizePath(value).Split('/').Select(Uri.EscapeDataString));
        private static StringContent JsonContent(object value) =>
            new StringContent(JsonConvert.SerializeObject(value), Encoding.UTF8, "application/json");

        private static async Task EnsureSuccessAsync(HttpResponseMessage response, string action)
        {
            if (!response.IsSuccessStatusCode)
            {
                string details = await response.Content.ReadAsStringAsync();
                throw new InvalidOperationException($"Failed to {action}: {(int)response.StatusCode} {response.ReasonPhrase}. {details}");
            }
        }
    }

    internal sealed class GoogleDriveStorageProvider : ICloudStorageProvider
    {
        private const string GoogleFolderMimeType = "application/vnd.google-apps.folder";
        private static readonly HttpClient Http = new HttpClient();
        private static readonly string[] Scopes =
        {
            "https://www.googleapis.com/auth/drive.file",
            "https://www.googleapis.com/auth/drive.metadata.readonly"
        };

        private readonly string clientId;
        private readonly string remotePath;
        private readonly CloudSecretStore secretStore;
        private string resolvedFolderId;

        public GoogleDriveStorageProvider(string clientId, string remotePath, CloudSecretStore secretStore)
        {
            if (string.IsNullOrWhiteSpace(clientId))
            {
                throw new InvalidOperationException("Google OAuth Client ID is required.");
            }

            this.clientId = clientId.Trim();
            this.remotePath = NormalizePath(remotePath);
            this.secretStore = secretStore;
        }

        public async Task<string> TestConnectionAsync()
        {
            string token = await AcquireAccessTokenAsync();
            using HttpRequestMessage request = CreateRequest(HttpMethod.Get, "https://www.googleapis.com/drive/v3/about?fields=user(displayName,emailAddress),storageQuota(limit,usage)", token);
            using HttpResponseMessage response = await Http.SendAsync(request);
            await EnsureSuccessAsync(response, "connect to Google Drive");
            GoogleDriveAboutResponse about = JsonConvert.DeserializeObject<GoogleDriveAboutResponse>(await response.Content.ReadAsStringAsync());

            string owner = about?.user?.displayName;
            if (string.IsNullOrWhiteSpace(owner))
            {
                owner = about?.user?.emailAddress;
            }

            if (about?.storageQuota?.limit > 0)
            {
                string used = CloudProviderFormatting.FormatBytes(about.storageQuota.usage);
                string total = CloudProviderFormatting.FormatBytes(about.storageQuota.limit);
                return string.IsNullOrWhiteSpace(owner)
                    ? $"Google Drive connected. Used {used} of {total}."
                    : $"Google Drive connected as {owner}. Used {used} of {total}.";
            }

            return string.IsNullOrWhiteSpace(owner)
                ? "Google Drive connection successful."
                : $"Google Drive connected as {owner}.";
        }

        public async Task EnsureContainerAsync()
        {
            resolvedFolderId = await ResolveFolderIdAsync(createMissing: true);
        }

        public async Task<string> DownloadTextAsync(string fileName, bool optional = false)
        {
            string token = await AcquireAccessTokenAsync();
            string parentId = await ResolveFolderIdAsync(createMissing: false);
            string fileId = await FindFileIdAsync(token, parentId, fileName);
            if (string.IsNullOrEmpty(fileId))
            {
                if (optional)
                {
                    return null;
                }

                throw new FileNotFoundException($"Google Drive file '{fileName}' was not found in '{remotePath}'.");
            }

            using HttpRequestMessage request = CreateRequest(HttpMethod.Get, $"https://www.googleapis.com/drive/v3/files/{fileId}?alt=media", token);
            using HttpResponseMessage response = await Http.SendAsync(request);
            await EnsureSuccessAsync(response, "download " + fileName);
            return await response.Content.ReadAsStringAsync();
        }

        public async Task UploadTextAsync(string fileName, string content)
        {
            string token = await AcquireAccessTokenAsync();
            string parentId = await ResolveFolderIdAsync(createMissing: true);
            string existingId = await FindFileIdAsync(token, parentId, fileName);
            string url = string.IsNullOrEmpty(existingId)
                ? "https://www.googleapis.com/upload/drive/v3/files?uploadType=multipart"
                : $"https://www.googleapis.com/upload/drive/v3/files/{existingId}?uploadType=multipart";
            HttpMethod method = string.IsNullOrEmpty(existingId) ? HttpMethod.Post : new HttpMethod("PATCH");

            using HttpRequestMessage request = CreateRequest(method, url, token);
            request.Content = BuildMultipartJsonContent(fileName, content, parentId, string.IsNullOrEmpty(existingId));
            using HttpResponseMessage response = await Http.SendAsync(request);
            await EnsureSuccessAsync(response, "upload " + fileName);
        }

        public async Task SignInAsync()
        {
            GoogleTokenState state = await AuthorizeInteractivelyAsync();
            SaveTokenState(state);
        }

        private async Task<string> AcquireAccessTokenAsync()
        {
            GoogleTokenState state = LoadTokenState();
            if (!string.IsNullOrWhiteSpace(state.AccessToken) && state.AccessTokenExpiresUtc > DateTimeOffset.UtcNow.AddMinutes(1))
            {
                return state.AccessToken;
            }

            if (!string.IsNullOrWhiteSpace(state.RefreshToken))
            {
                GoogleTokenState refreshed = await RefreshTokenAsync(state.RefreshToken);
                refreshed.RefreshToken = string.IsNullOrWhiteSpace(refreshed.RefreshToken) ? state.RefreshToken : refreshed.RefreshToken;
                SaveTokenState(refreshed);
                return refreshed.AccessToken;
            }

            throw new InvalidOperationException("Google Drive is not connected yet. Click 'Sign in to Google account' first.");
        }

        private async Task<GoogleTokenState> AuthorizeInteractivelyAsync()
        {
            string verifier = CreateCodeVerifier();
            string challenge = CreateCodeChallenge(verifier);
            int port = GetFreePort();
            string redirectUri = $"http://127.0.0.1:{port}/";
            string state = Guid.NewGuid().ToString("N");
            string authUrl =
                "https://accounts.google.com/o/oauth2/v2/auth"
                + "?client_id=" + Uri.EscapeDataString(clientId)
                + "&redirect_uri=" + Uri.EscapeDataString(redirectUri)
                + "&response_type=code"
                + "&scope=" + Uri.EscapeDataString(string.Join(" ", Scopes))
                + "&access_type=offline"
                + "&prompt=consent"
                + "&code_challenge=" + Uri.EscapeDataString(challenge)
                + "&code_challenge_method=S256"
                + "&state=" + Uri.EscapeDataString(state);

            using HttpListener listener = new HttpListener();
            listener.Prefixes.Add(redirectUri);
            listener.Start();

            Process.Start(new ProcessStartInfo
            {
                FileName = authUrl,
                UseShellExecute = true
            });

            HttpListenerContext context = await listener.GetContextAsync();
            string error = context.Request.QueryString["error"];
            string returnedState = context.Request.QueryString["state"];
            string code = context.Request.QueryString["code"];
            await WriteBrowserResponseAsync(context.Response, string.IsNullOrWhiteSpace(error)
                ? "Google Drive was connected. You can return to SDA++."
                : "Google Drive sign-in failed. You can close this window and return to SDA++.");
            listener.Stop();

            if (!string.IsNullOrWhiteSpace(error))
            {
                throw new InvalidOperationException("Google sign-in failed: " + error + ".");
            }

            if (!string.Equals(state, returnedState, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Google sign-in failed: state verification mismatch.");
            }

            if (string.IsNullOrWhiteSpace(code))
            {
                throw new InvalidOperationException("Google sign-in failed: no authorization code was returned.");
            }

            using FormUrlEncodedContent body = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["code"] = code,
                ["client_id"] = clientId,
                ["redirect_uri"] = redirectUri,
                ["grant_type"] = "authorization_code",
                ["code_verifier"] = verifier
            });
            using HttpResponseMessage tokenResponse = await Http.PostAsync("https://oauth2.googleapis.com/token", body);
            await EnsureSuccessAsync(tokenResponse, "exchange Google authorization code");
            GoogleTokenResponse token = JsonConvert.DeserializeObject<GoogleTokenResponse>(await tokenResponse.Content.ReadAsStringAsync());
            return ConvertTokenResponse(token);
        }

        private async Task<GoogleTokenState> RefreshTokenAsync(string refreshToken)
        {
            using FormUrlEncodedContent body = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["client_id"] = clientId,
                ["refresh_token"] = refreshToken,
                ["grant_type"] = "refresh_token"
            });
            using HttpResponseMessage response = await Http.PostAsync("https://oauth2.googleapis.com/token", body);
            await EnsureSuccessAsync(response, "refresh Google Drive token");
            GoogleTokenResponse token = JsonConvert.DeserializeObject<GoogleTokenResponse>(await response.Content.ReadAsStringAsync());
            return ConvertTokenResponse(token);
        }

        private async Task<string> ResolveFolderIdAsync(bool createMissing)
        {
            if (!string.IsNullOrEmpty(resolvedFolderId))
            {
                return resolvedFolderId;
            }

            string token = await AcquireAccessTokenAsync();
            string parentId = "root";
            if (string.IsNullOrWhiteSpace(remotePath))
            {
                resolvedFolderId = parentId;
                return resolvedFolderId;
            }

            foreach (string segment in remotePath.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string folderId = await FindFolderIdAsync(token, parentId, segment);
                if (string.IsNullOrEmpty(folderId))
                {
                    if (!createMissing)
                    {
                        return null;
                    }

                    folderId = await CreateFolderAsync(token, parentId, segment);
                }

                parentId = folderId;
            }

            resolvedFolderId = parentId;
            return resolvedFolderId;
        }

        private async Task<string> FindFolderIdAsync(string token, string parentId, string folderName)
        {
            string query = $"name = '{EscapeQueryValue(folderName)}' and mimeType = '{GoogleFolderMimeType}' and trashed = false and '{parentId}' in parents";
            using HttpRequestMessage request = CreateRequest(HttpMethod.Get, "https://www.googleapis.com/drive/v3/files?q=" + Uri.EscapeDataString(query) + "&fields=files(id,name)&pageSize=1", token);
            using HttpResponseMessage response = await Http.SendAsync(request);
            await EnsureSuccessAsync(response, "find Google Drive folder");
            GoogleDriveFilesResponse payload = JsonConvert.DeserializeObject<GoogleDriveFilesResponse>(await response.Content.ReadAsStringAsync());
            return payload?.files?.FirstOrDefault()?.id;
        }

        private async Task<string> FindFileIdAsync(string token, string parentId, string fileName)
        {
            string query = $"name = '{EscapeQueryValue(fileName)}' and trashed = false and '{parentId}' in parents";
            using HttpRequestMessage request = CreateRequest(HttpMethod.Get, "https://www.googleapis.com/drive/v3/files?q=" + Uri.EscapeDataString(query) + "&fields=files(id,name)&pageSize=1", token);
            using HttpResponseMessage response = await Http.SendAsync(request);
            await EnsureSuccessAsync(response, "find Google Drive file");
            GoogleDriveFilesResponse payload = JsonConvert.DeserializeObject<GoogleDriveFilesResponse>(await response.Content.ReadAsStringAsync());
            return payload?.files?.FirstOrDefault()?.id;
        }

        private async Task<string> CreateFolderAsync(string token, string parentId, string folderName)
        {
            using HttpRequestMessage request = CreateRequest(HttpMethod.Post, "https://www.googleapis.com/drive/v3/files?fields=id", token);
            request.Content = new StringContent(JsonConvert.SerializeObject(new
            {
                name = folderName,
                mimeType = GoogleFolderMimeType,
                parents = new[] { parentId }
            }), Encoding.UTF8, "application/json");
            using HttpResponseMessage response = await Http.SendAsync(request);
            await EnsureSuccessAsync(response, "create Google Drive folder");
            GoogleDriveFile payload = JsonConvert.DeserializeObject<GoogleDriveFile>(await response.Content.ReadAsStringAsync());
            return payload?.id;
        }

        private static MultipartContent BuildMultipartJsonContent(string fileName, string content, string parentId, bool includeParents)
        {
            MultipartContent multipart = new MultipartContent("related", "sda-boundary-" + Guid.NewGuid().ToString("N"));
            var metadata = includeParents
                ? new Dictionary<string, object> { ["name"] = fileName, ["parents"] = new[] { parentId } }
                : new Dictionary<string, object> { ["name"] = fileName };
            multipart.Add(new StringContent(JsonConvert.SerializeObject(metadata), Encoding.UTF8, "application/json"));
            multipart.Add(new StringContent(content ?? string.Empty, Encoding.UTF8, "application/json"));
            return multipart;
        }

        private GoogleTokenState LoadTokenState()
        {
            string json = secretStore.Load("gdrive-token-" + clientId);
            return string.IsNullOrWhiteSpace(json)
                ? new GoogleTokenState()
                : JsonConvert.DeserializeObject<GoogleTokenState>(json) ?? new GoogleTokenState();
        }

        private void SaveTokenState(GoogleTokenState state)
        {
            secretStore.Save("gdrive-token-" + clientId, JsonConvert.SerializeObject(state));
        }

        private static GoogleTokenState ConvertTokenResponse(GoogleTokenResponse token)
        {
            if (token == null || string.IsNullOrWhiteSpace(token.access_token))
            {
                throw new InvalidOperationException("Google Drive did not return a valid access token.");
            }

            return new GoogleTokenState
            {
                AccessToken = token.access_token,
                RefreshToken = token.refresh_token ?? string.Empty,
                AccessTokenExpiresUtc = DateTimeOffset.UtcNow.AddSeconds(Math.Max(60, token.expires_in))
            };
        }

        private static HttpRequestMessage CreateRequest(HttpMethod method, string url, string token)
        {
            HttpRequestMessage request = new HttpRequestMessage(method, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            return request;
        }

        private static string NormalizePath(string value) => (value ?? string.Empty).Trim().Trim('/');

        private static string EscapeQueryValue(string value) => (value ?? string.Empty).Replace("\\", "\\\\").Replace("'", "\\'");

        private static string CreateCodeVerifier()
        {
            byte[] buffer = new byte[64];
            RandomNumberGenerator.Fill(buffer);
            return Base64UrlEncode(buffer);
        }

        private static string CreateCodeChallenge(string verifier)
        {
            using SHA256 sha = SHA256.Create();
            return Base64UrlEncode(sha.ComputeHash(Encoding.ASCII.GetBytes(verifier)));
        }

        private static string Base64UrlEncode(byte[] bytes)
        {
            return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        }

        private static int GetFreePort()
        {
            TcpListener listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            return port;
        }

        private static async Task WriteBrowserResponseAsync(HttpListenerResponse response, string message)
        {
            response.StatusCode = 200;
            response.ContentType = "text/html; charset=utf-8";
            string html = "<html><body style=\"font-family:Segoe UI,Arial,sans-serif;padding:24px;background:#0f1219;color:#f2f5ff;\">"
                + WebUtility.HtmlEncode(message)
                + "</body></html>";
            byte[] data = Encoding.UTF8.GetBytes(html);
            response.ContentLength64 = data.Length;
            await response.OutputStream.WriteAsync(data, 0, data.Length);
            response.OutputStream.Close();
        }

        private static async Task EnsureSuccessAsync(HttpResponseMessage response, string action)
        {
            if (!response.IsSuccessStatusCode)
            {
                string details = await response.Content.ReadAsStringAsync();
                throw new InvalidOperationException($"Failed to {action}: {(int)response.StatusCode} {response.ReasonPhrase}. {details}");
            }
        }

        private sealed class GoogleTokenState
        {
            public string AccessToken { get; set; } = string.Empty;
            public string RefreshToken { get; set; } = string.Empty;
            public DateTimeOffset AccessTokenExpiresUtc { get; set; }
        }

        private sealed class GoogleTokenResponse
        {
            public string access_token { get; set; }
            public string refresh_token { get; set; }
            public int expires_in { get; set; }
        }
    }

    internal sealed class DropboxSpaceUsageResponse
    {
        public long used { get; set; }
        public DropboxAllocation allocation { get; set; }
    }

    internal sealed class DropboxAllocation
    {
        public long allocated { get; set; }
    }

    internal sealed class OneDriveDriveResponse
    {
        public OneDriveQuota quota { get; set; }
    }

    internal sealed class OneDriveQuota
    {
        public long total { get; set; }
        public long used { get; set; }
    }

    internal sealed class GoogleDriveAboutResponse
    {
        public GoogleDriveUser user { get; set; }
        public GoogleDriveStorageQuota storageQuota { get; set; }
    }

    internal sealed class GoogleDriveUser
    {
        public string displayName { get; set; }
        public string emailAddress { get; set; }
    }

    internal sealed class GoogleDriveStorageQuota
    {
        public long limit { get; set; }
        public long usage { get; set; }
    }

    internal sealed class GoogleDriveFilesResponse
    {
        public List<GoogleDriveFile> files { get; set; }
    }

    internal sealed class GoogleDriveFile
    {
        public string id { get; set; }
        public string name { get; set; }
    }

    internal static class CloudProviderFormatting
    {
        public static string FormatBytes(long bytes)
        {
            double value = bytes;
            string[] units = { "B", "KB", "MB", "GB", "TB" };
            int unit = 0;
            while (value >= 1024 && unit < units.Length - 1)
            {
                value /= 1024d;
                unit++;
            }

            return $"{value:0.#} {units[unit]}";
        }
    }
}
