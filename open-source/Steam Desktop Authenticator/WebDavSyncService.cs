using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;

namespace Steam_Desktop_Authenticator
{
    internal sealed class WebDavSyncService
    {
        private const string ManifestFileName = "manifest.json";
        private const string CredentialsFileName = "credentials.json";

        private readonly WebDavSecretStore secretStore = new WebDavSecretStore();

        public sealed class WebDavSettings
        {
            public string Url { get; set; }
            public string Username { get; set; }
            public string Password { get; set; }
            public string RemotePath { get; set; }
            public bool SyncStoredCredentials { get; set; }
        }

        public async Task TestConnectionAsync(WebDavSettings settings)
        {
            using HttpClient client = CreateClient(settings);
            using HttpRequestMessage request = new HttpRequestMessage(new HttpMethod("PROPFIND"), BuildDirectoryUri(settings, settings.RemotePath));
            request.Headers.Add("Depth", "0");
            request.Content = new StringContent("<?xml version=\"1.0\" encoding=\"utf-8\" ?><d:propfind xmlns:d=\"DAV:\"><d:prop><d:displayname/></d:prop></d:propfind>", Encoding.UTF8, "application/xml");

            using HttpResponseMessage response = await client.SendAsync(request);
            if (!response.IsSuccessStatusCode && response.StatusCode != System.Net.HttpStatusCode.MultiStatus)
            {
                throw new InvalidOperationException("WebDAV connection failed: " + (int)response.StatusCode + " " + response.ReasonPhrase);
            }
        }

        public async Task PullAsync(WebDavSettings settings)
        {
            string maDir = Path.Combine(Manifest.GetExecutableDir(), "maFiles");
            Directory.CreateDirectory(maDir);
            string backupDir = CreateBackupDirectory("pull");
            BackupFileIfExists(Path.Combine(maDir, ManifestFileName), backupDir);
            foreach (string file in Directory.GetFiles(maDir, "*.maFile"))
            {
                BackupFileIfExists(file, backupDir);
            }

            if (settings.SyncStoredCredentials)
            {
                BackupFileIfExists(Path.Combine(maDir, CredentialsFileName), backupDir);
            }

            using HttpClient client = CreateClient(settings);
            string manifestJson = await DownloadTextAsync(client, BuildFileUri(settings, ManifestFileName));
            Manifest remoteManifest = JsonConvert.DeserializeObject<Manifest>(manifestJson);
            if (remoteManifest == null)
            {
                throw new InvalidOperationException("Cloud manifest is invalid.");
            }

            File.WriteAllText(Path.Combine(maDir, ManifestFileName), manifestJson);

            foreach (Manifest.ManifestEntry entry in remoteManifest.Entries ?? new List<Manifest.ManifestEntry>())
            {
                string contents = await DownloadTextAsync(client, BuildFileUri(settings, entry.Filename));
                File.WriteAllText(Path.Combine(maDir, entry.Filename), contents);
            }

            if (settings.SyncStoredCredentials)
            {
                Uri remoteCredentialsUri = BuildFileUri(settings, CredentialsFileName);
                string credentialsJson = await TryDownloadTextAsync(client, remoteCredentialsUri);
                if (credentialsJson != null)
                {
                    File.WriteAllText(Path.Combine(maDir, CredentialsFileName), credentialsJson);
                }
            }
        }

        public async Task PushAsync(WebDavSettings settings)
        {
            string maDir = Path.Combine(Manifest.GetExecutableDir(), "maFiles");
            string manifestPath = Path.Combine(maDir, ManifestFileName);
            if (!File.Exists(manifestPath))
            {
                throw new FileNotFoundException("Local manifest.json was not found.", manifestPath);
            }

            await EnsureRemoteDirectoryExistsAsync(settings);

            string manifestJson = File.ReadAllText(manifestPath);
            Manifest localManifest = JsonConvert.DeserializeObject<Manifest>(manifestJson);
            if (localManifest == null)
            {
                throw new InvalidOperationException("Local manifest is invalid.");
            }

            using HttpClient client = CreateClient(settings);
            foreach (Manifest.ManifestEntry entry in localManifest.Entries ?? new List<Manifest.ManifestEntry>())
            {
                string filePath = Path.Combine(maDir, entry.Filename);
                if (!File.Exists(filePath))
                {
                    continue;
                }

                await UploadTextAsync(client, BuildFileUri(settings, entry.Filename), File.ReadAllText(filePath));
            }

            if (settings.SyncStoredCredentials)
            {
                string credentialsPath = Path.Combine(maDir, CredentialsFileName);
                if (File.Exists(credentialsPath))
                {
                    await UploadTextAsync(client, BuildFileUri(settings, CredentialsFileName), File.ReadAllText(credentialsPath));
                }
            }

            await UploadTextAsync(client, BuildFileUri(settings, ManifestFileName), manifestJson);
        }

        public string LoadSavedPassword()
        {
            return secretStore.LoadPassword();
        }

        public void SavePassword(string password)
        {
            secretStore.SavePassword(password);
        }

        private static HttpClient CreateClient(WebDavSettings settings)
        {
            ValidateSettings(settings, requirePassword: true);

            HttpClient client = new HttpClient();
            string raw = settings.Username + ":" + settings.Password;
            string auth = Convert.ToBase64String(Encoding.UTF8.GetBytes(raw));
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", auth);
            return client;
        }

        private static void ValidateSettings(WebDavSettings settings, bool requirePassword)
        {
            if (settings == null)
            {
                throw new InvalidOperationException("WebDAV settings are missing.");
            }

            if (string.IsNullOrWhiteSpace(settings.Url))
            {
                throw new InvalidOperationException("WebDAV URL is required.");
            }

            if (string.IsNullOrWhiteSpace(settings.Username))
            {
                throw new InvalidOperationException("WebDAV username is required.");
            }

            if (requirePassword && string.IsNullOrWhiteSpace(settings.Password))
            {
                throw new InvalidOperationException("WebDAV password is required.");
            }
        }

        private async Task EnsureRemoteDirectoryExistsAsync(WebDavSettings settings)
        {
            using HttpClient client = CreateClient(settings);
            Uri baseUri = NormalizeBaseUri(settings.Url);
            string remotePath = NormalizeRemotePath(settings.RemotePath);
            if (string.IsNullOrEmpty(remotePath))
            {
                return;
            }

            string[] segments = remotePath.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
            string currentPath = string.Empty;
            foreach (string segment in segments)
            {
                currentPath += segment + "/";
                Uri uri = new Uri(baseUri, currentPath);
                using HttpRequestMessage mkcol = new HttpRequestMessage(new HttpMethod("MKCOL"), uri);
                using HttpResponseMessage response = await client.SendAsync(mkcol);
                if (!response.IsSuccessStatusCode &&
                    response.StatusCode != System.Net.HttpStatusCode.MethodNotAllowed &&
                    response.StatusCode != System.Net.HttpStatusCode.Conflict)
                {
                    throw new InvalidOperationException("Failed to create remote folder: " + response.ReasonPhrase);
                }
            }
        }

        private static string CreateBackupDirectory(string action)
        {
            string path = Path.Combine(Manifest.GetExecutableDir(), "maFiles", "backups", "cloudsync", DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + action);
            Directory.CreateDirectory(path);
            return path;
        }

        private static void BackupFileIfExists(string sourceFile, string backupDir)
        {
            if (!File.Exists(sourceFile))
            {
                return;
            }

            File.Copy(sourceFile, Path.Combine(backupDir, Path.GetFileName(sourceFile)), true);
        }

        private static Uri NormalizeBaseUri(string url)
        {
            string normalized = (url ?? string.Empty).Trim();
            if (!normalized.EndsWith("/", StringComparison.Ordinal))
            {
                normalized += "/";
            }

            return new Uri(normalized, UriKind.Absolute);
        }

        private static string NormalizeRemotePath(string remotePath)
        {
            return (remotePath ?? string.Empty).Trim().Trim('/');
        }

        private static Uri BuildDirectoryUri(WebDavSettings settings, string remotePath)
        {
            Uri baseUri = NormalizeBaseUri(settings.Url);
            string normalizedPath = NormalizeRemotePath(remotePath);
            if (string.IsNullOrEmpty(normalizedPath))
            {
                return baseUri;
            }

            return new Uri(baseUri, normalizedPath + "/");
        }

        private static Uri BuildFileUri(WebDavSettings settings, string fileName)
        {
            Uri directoryUri = BuildDirectoryUri(settings, settings.RemotePath);
            return new Uri(directoryUri, fileName);
        }

        private static async Task<string> DownloadTextAsync(HttpClient client, Uri uri)
        {
            using HttpResponseMessage response = await client.GetAsync(uri);
            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException("Failed to download " + uri + ": " + (int)response.StatusCode + " " + response.ReasonPhrase);
            }

            return await response.Content.ReadAsStringAsync();
        }

        private static async Task<string> TryDownloadTextAsync(HttpClient client, Uri uri)
        {
            using HttpResponseMessage response = await client.GetAsync(uri);
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                return null;
            }

            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException("Failed to download " + uri + ": " + (int)response.StatusCode + " " + response.ReasonPhrase);
            }

            return await response.Content.ReadAsStringAsync();
        }

        private static async Task UploadTextAsync(HttpClient client, Uri uri, string content)
        {
            using StringContent body = new StringContent(content, Encoding.UTF8, "application/json");
            using HttpResponseMessage response = await client.PutAsync(uri, body);
            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException("Failed to upload " + uri + ": " + (int)response.StatusCode + " " + response.ReasonPhrase);
            }
        }
    }
}
