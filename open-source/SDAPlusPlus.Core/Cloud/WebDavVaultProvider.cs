using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Xml.Linq;
using Newtonsoft.Json.Linq;

namespace SDAPlusPlus.Core.Cloud;

public sealed class WebDavVaultProvider(HttpClient httpClient) : ICloudVaultProvider
{
    private static readonly HttpMethod PropFind = new("PROPFIND");
    private const int MaximumAccountCount = 500;
    private const int MaximumDocumentCharacters = 2 * 1024 * 1024;

    public async Task<CloudConnectionResult> TestConnectionAsync(
        CloudConnectionSettings settings,
        CancellationToken cancellationToken = default)
    {
        if (!TryValidate(settings, out string error))
        {
            return new CloudConnectionResult(false, error);
        }

        try
        {
            using HttpRequestMessage request = CreateRequest(PropFind, BuildDirectoryUri(settings), settings);
            request.Headers.TryAddWithoutValidation("Depth", "0");
            request.Content = CreatePropFindBody();
            using HttpResponseMessage response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            return IsWebDavSuccess(response.StatusCode)
                ? new CloudConnectionResult(true, "WebDAV connection established.")
                : new CloudConnectionResult(false, $"WebDAV returned HTTP {(int)response.StatusCode}.");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new CloudConnectionResult(false, "WebDAV request timed out.");
        }
        catch (HttpRequestException exception)
        {
            return new CloudConnectionResult(false, "WebDAV connection failed: " + exception.Message);
        }
    }

    public async Task<CloudPullPreview> PreviewPullAsync(
        CloudConnectionSettings settings,
        CancellationToken cancellationToken = default)
    {
        CloudConnectionResult connection = await TestConnectionAsync(settings, cancellationToken);
        if (!connection.Success)
        {
            return new CloudPullPreview(false, connection.Message);
        }

        try
        {
            Uri manifestUri = new(BuildDirectoryUri(settings), "manifest.json");
            using HttpRequestMessage manifestRequest = CreateRequest(HttpMethod.Get, manifestUri, settings);
            using HttpResponseMessage manifestResponse = await httpClient.SendAsync(manifestRequest, cancellationToken);
            if (manifestResponse.IsSuccessStatusCode)
            {
                string json = await manifestResponse.Content.ReadAsStringAsync(cancellationToken);
                return ParseManifestPreview(json);
            }

            if (manifestResponse.StatusCode != HttpStatusCode.NotFound)
            {
                return new CloudPullPreview(false, $"manifest.json returned HTTP {(int)manifestResponse.StatusCode}.");
            }

            return await PreviewUsingPropFindAsync(settings, cancellationToken);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new CloudPullPreview(false, "WebDAV request timed out.");
        }
        catch (Exception exception) when (exception is HttpRequestException or FormatException)
        {
            return new CloudPullPreview(false, "Cloud preview failed: " + exception.Message);
        }
    }

    public async Task<CloudPullPackageResult> PreparePullAsync(
        CloudConnectionSettings settings,
        CancellationToken cancellationToken = default)
    {
        CloudPullPreview preview = await PreviewPullAsync(settings, cancellationToken);
        if (!preview.Success)
        {
            return new CloudPullPackageResult(false, preview.Message);
        }

        IReadOnlyList<string> fileNames = preview.FileNames ?? [];
        if (fileNames.Count > MaximumAccountCount)
        {
            return new CloudPullPackageResult(false, $"Cloud backup contains more than {MaximumAccountCount} accounts.");
        }

        try
        {
            string manifestJson;
            if (preview.ManifestFound)
            {
                using HttpRequestMessage request = CreateRequest(HttpMethod.Get, new Uri(BuildDirectoryUri(settings), "manifest.json"), settings);
                using HttpResponseMessage response = await httpClient.SendAsync(request, cancellationToken);
                if (!response.IsSuccessStatusCode)
                {
                    return new CloudPullPackageResult(false, $"manifest.json returned HTTP {(int)response.StatusCode}.");
                }

                manifestJson = await ReadLimitedTextAsync(response, "manifest.json", cancellationToken);
            }
            else
            {
                manifestJson = CreateFallbackManifest(fileNames);
            }

            var accountFiles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (string fileName in fileNames)
            {
                if (!IsSafeMaFileName(fileName))
                {
                    return new CloudPullPackageResult(false, "Cloud backup contains an unsafe account filename.");
                }

                Uri fileUri = new(BuildDirectoryUri(settings), Uri.EscapeDataString(fileName));
                using HttpRequestMessage request = CreateRequest(HttpMethod.Get, fileUri, settings);
                using HttpResponseMessage response = await httpClient.SendAsync(request, cancellationToken);
                if (!response.IsSuccessStatusCode)
                {
                    return new CloudPullPackageResult(false, $"{fileName} returned HTTP {(int)response.StatusCode}.");
                }

                accountFiles[fileName] = await ReadLimitedTextAsync(response, fileName, cancellationToken);
            }

            return new CloudPullPackageResult(
                true,
                "Cloud backup downloaded and validated in memory.",
                new CloudPullPackage(manifestJson, accountFiles, preview.IsEncrypted));
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new CloudPullPackageResult(false, "WebDAV request timed out.");
        }
        catch (Exception exception) when (exception is HttpRequestException or InvalidDataException)
        {
            return new CloudPullPackageResult(false, exception.Message);
        }
    }

    public async Task<CloudPushResult> PushAsync(CloudConnectionSettings settings, CloudPushPackage package, CancellationToken cancellationToken = default)
    {
        if (!TryValidate(settings, out string error)) return new CloudPushResult(false, error);
        if (!package.IsEncrypted) return new CloudPushResult(false, "Cloud upload requires an encrypted vault.");
        try
        {
            await EnsureDirectoryAsync(settings, cancellationToken);
            foreach ((string name, string contents) in package.AccountFiles)
                await PutAsync(settings, name, contents, cancellationToken);
            await PutAsync(settings, "manifest.json", package.ManifestJson, cancellationToken);
            int accountCount = package.AccountFiles.Keys.Count(IsSafeMaFileName);
            return new CloudPushResult(true, $"Encrypted vault uploaded. Accounts: {accountCount}.", accountCount);
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or TaskCanceledException)
        { return new CloudPushResult(false, ex.Message); }
    }

    internal async Task<CloudFileResult> UploadFileAsync(
        CloudConnectionSettings settings,
        string fileName,
        string contents,
        CancellationToken cancellationToken = default)
    {
        if (!TryValidate(settings, out string error)) return new CloudFileResult(false, error);
        if (!string.Equals(fileName, "credentials.secure.json", StringComparison.OrdinalIgnoreCase))
            return new CloudFileResult(false, "Unsupported cloud file name.");
        if (string.IsNullOrWhiteSpace(contents) || contents.Length > MaximumDocumentCharacters)
            return new CloudFileResult(false, "The encrypted credentials file is empty or too large.");
        try
        {
            await EnsureDirectoryAsync(settings, cancellationToken);
            await PutAsync(settings, fileName, contents, cancellationToken);
            return new CloudFileResult(true, fileName + " uploaded securely.");
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or TaskCanceledException)
        {
            return new CloudFileResult(false, ex.Message);
        }
    }

    private async Task EnsureDirectoryAsync(CloudConnectionSettings settings, CancellationToken token)
    {
        Uri root = new(settings.Endpoint.Trim().TrimEnd('/') + "/");
        string current = string.Empty;
        foreach (string segment in (settings.RemotePath ?? string.Empty).Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            current += Uri.EscapeDataString(segment) + "/";
            using HttpRequestMessage request = CreateRequest(new HttpMethod("MKCOL"), new Uri(root, current), settings);
            using HttpResponseMessage response = await httpClient.SendAsync(request, token);
            if (!response.IsSuccessStatusCode && response.StatusCode is not (HttpStatusCode.MethodNotAllowed or HttpStatusCode.Conflict))
                throw new InvalidOperationException($"WebDAV folder creation returned HTTP {(int)response.StatusCode}.");
        }
    }

    private async Task PutAsync(CloudConnectionSettings settings, string name, string contents, CancellationToken token)
    {
        using HttpRequestMessage request = CreateRequest(HttpMethod.Put, new Uri(BuildDirectoryUri(settings), Uri.EscapeDataString(name)), settings);
        request.Content = new StringContent(contents, Encoding.UTF8, "application/json");
        using HttpResponseMessage response = await httpClient.SendAsync(request, token);
        if (!response.IsSuccessStatusCode) throw new InvalidOperationException($"WebDAV upload of {name} returned HTTP {(int)response.StatusCode}.");
    }

    private async Task<CloudPullPreview> PreviewUsingPropFindAsync(
        CloudConnectionSettings settings,
        CancellationToken cancellationToken)
    {
        using HttpRequestMessage request = CreateRequest(PropFind, BuildDirectoryUri(settings), settings);
        request.Headers.TryAddWithoutValidation("Depth", "1");
        request.Content = CreatePropFindBody();
        using HttpResponseMessage response = await httpClient.SendAsync(request, cancellationToken);
        if (!IsWebDavSuccess(response.StatusCode))
        {
            return new CloudPullPreview(false, $"WebDAV file listing returned HTTP {(int)response.StatusCode}.");
        }

        string xml = await response.Content.ReadAsStringAsync(cancellationToken);
        IReadOnlyList<string> files = ParseMaFileNames(xml);
        return files.Count == 0
            ? new CloudPullPreview(false, "The cloud folder is reachable, but no .maFile accounts were found.")
            : new CloudPullPreview(true, "Cloud account files found through WebDAV listing.", files.Count, ManifestFound: false, FileNames: files);
    }

    private static CloudPullPreview ParseManifestPreview(string json)
    {
        try
        {
            JObject manifest = JObject.Parse(json);
            bool encrypted = manifest.Value<bool?>("encrypted") ?? false;
            var files = (manifest["entries"] as JArray ?? [])
                .Select(entry => entry?["filename"]?.Value<string>())
                .Where(IsSafeMaFileName)
                .Cast<string>()
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            return new CloudPullPreview(
                true,
                files.Length == 0 ? "Cloud manifest is valid but contains no accounts." : "Cloud manifest is valid.",
                files.Length,
                encrypted,
                ManifestFound: true,
                FileNames: files);
        }
        catch (Exception exception) when (exception is Newtonsoft.Json.JsonException or InvalidOperationException)
        {
            return new CloudPullPreview(false, "Cloud manifest is invalid.");
        }
    }

    private static async Task<string> ReadLimitedTextAsync(
        HttpResponseMessage response,
        string fileName,
        CancellationToken cancellationToken)
    {
        if (response.Content.Headers.ContentLength > MaximumDocumentCharacters)
        {
            throw new InvalidDataException(fileName + " exceeds the allowed size.");
        }

        string contents = await response.Content.ReadAsStringAsync(cancellationToken);
        if (contents.Length == 0 || contents.Length > MaximumDocumentCharacters)
        {
            throw new InvalidDataException(fileName + " is empty or exceeds the allowed size.");
        }

        return contents;
    }

    private static string CreateFallbackManifest(IReadOnlyList<string> fileNames)
    {
        var entries = new JArray(fileNames.Select(fileName => new JObject
        {
            ["filename"] = fileName,
            ["steamid"] = ulong.TryParse(Path.GetFileNameWithoutExtension(fileName), out ulong steamId) ? steamId : 0,
            ["favorite"] = false
        }));
        return new JObject
        {
            ["encrypted"] = false,
            ["first_run"] = false,
            ["entries"] = entries
        }.ToString(Newtonsoft.Json.Formatting.Indented);
    }

    private static IReadOnlyList<string> ParseMaFileNames(string xml)
    {
        try
        {
            XDocument document = XDocument.Parse(xml, LoadOptions.None);
            XNamespace dav = "DAV:";
            return document.Descendants(dav + "href")
                .Select(node => Uri.UnescapeDataString(node.Value))
                .Select(value => Path.GetFileName(value.TrimEnd('/')))
                .Where(IsSafeMaFileName)
                .Cast<string>()
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        catch (System.Xml.XmlException)
        {
            return [];
        }
    }

    private static bool TryValidate(CloudConnectionSettings settings, out string error)
    {
        error = string.Empty;
        if (!Uri.TryCreate(settings.Endpoint?.Trim(), UriKind.Absolute, out Uri? endpoint) || endpoint.Scheme != Uri.UriSchemeHttps)
        {
            error = "A valid HTTPS WebDAV URL is required.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(settings.Username))
        {
            error = "WebDAV username is required.";
            return false;
        }

        if (string.IsNullOrEmpty(settings.Password))
        {
            error = "WebDAV password is required.";
            return false;
        }

        return true;
    }

    private static Uri BuildDirectoryUri(CloudConnectionSettings settings)
    {
        string endpoint = settings.Endpoint.Trim().TrimEnd('/') + "/";
        Uri baseUri = new(endpoint, UriKind.Absolute);
        string path = string.Join('/', (settings.RemotePath ?? string.Empty)
            .Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(segment => segment is not "." and not "..")
            .Select(Uri.EscapeDataString));
        return string.IsNullOrEmpty(path) ? baseUri : new Uri(baseUri, path + "/");
    }

    private static HttpRequestMessage CreateRequest(HttpMethod method, Uri uri, CloudConnectionSettings settings)
    {
        var request = new HttpRequestMessage(method, uri);
        string credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes(settings.Username + ":" + settings.Password));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
        return request;
    }

    private static StringContent CreatePropFindBody() => new(
        "<?xml version=\"1.0\" encoding=\"utf-8\"?><d:propfind xmlns:d=\"DAV:\"><d:prop><d:displayname/><d:resourcetype/></d:prop></d:propfind>",
        Encoding.UTF8,
        "application/xml");

    private static bool IsWebDavSuccess(HttpStatusCode statusCode) =>
        statusCode == HttpStatusCode.MultiStatus || (int)statusCode is >= 200 and < 300;

    private static bool IsSafeMaFileName(string? filename) =>
        !string.IsNullOrWhiteSpace(filename) &&
        filename.EndsWith(".maFile", StringComparison.OrdinalIgnoreCase) &&
        filename == Path.GetFileName(filename) &&
        !filename.Contains("..", StringComparison.Ordinal);
}
