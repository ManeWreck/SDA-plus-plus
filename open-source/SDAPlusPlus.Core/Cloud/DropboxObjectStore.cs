using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace SDAPlusPlus.Core.Cloud;

internal sealed class DropboxObjectStore : ICloudObjectStore
{
    private readonly HttpClient _http = new();
    private readonly string _token;
    private readonly string _path;

    public DropboxObjectStore(CloudConnectionSettings settings)
    {
        _token = settings.Password?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(_token)) throw new InvalidOperationException("Dropbox access token is required.");
        _path = string.IsNullOrWhiteSpace(settings.RemotePath) ? string.Empty : "/" + settings.RemotePath.Trim().Trim('/');
    }

    public async Task<string> TestAsync(CancellationToken cancellationToken)
    {
        using HttpRequestMessage request = Request(HttpMethod.Post, "https://api.dropboxapi.com/2/users/get_space_usage");
        request.Content = new StringContent("null", Encoding.UTF8, "application/json");
        using HttpResponseMessage response = await _http.SendAsync(request, cancellationToken);
        await EnsureSuccess(response, "connect to Dropbox", cancellationToken);
        JObject usage = JObject.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        long used = usage.Value<long?>("used") ?? 0;
        long allocated = usage["allocation"]?.Value<long?>("allocated") ?? 0;
        return allocated > 0 ? $"Dropbox connected. Used {FormatBytes(used)} of {FormatBytes(allocated)}." : "Dropbox connected.";
    }

    public async Task EnsureContainerAsync(CancellationToken cancellationToken)
    {
        string current = string.Empty;
        foreach (string segment in _path.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            current += "/" + segment;
            using HttpRequestMessage request = Request(HttpMethod.Post, "https://api.dropboxapi.com/2/files/create_folder_v2");
            request.Content = new StringContent(JsonConvert.SerializeObject(new { path = current, autorename = false }), Encoding.UTF8, "application/json");
            using HttpResponseMessage response = await _http.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                string details = await response.Content.ReadAsStringAsync(cancellationToken);
                if (!details.Contains("conflict", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException($"Failed to create Dropbox folder: HTTP {(int)response.StatusCode}. {details}");
            }
        }
    }

    public async Task<IReadOnlyList<string>> ListAsync(CancellationToken cancellationToken)
    {
        List<string> names = [];
        string? cursor = null;
        bool more;
        do
        {
            string endpoint = cursor is null ? "https://api.dropboxapi.com/2/files/list_folder" : "https://api.dropboxapi.com/2/files/list_folder/continue";
            using HttpRequestMessage request = Request(HttpMethod.Post, endpoint);
            request.Content = new StringContent(JsonConvert.SerializeObject(cursor is null ? new { path = _path, recursive = false } : new { cursor }), Encoding.UTF8, "application/json");
            using HttpResponseMessage response = await _http.SendAsync(request, cancellationToken);
            await EnsureSuccess(response, "list Dropbox folder", cancellationToken);
            JObject payload = JObject.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
            names.AddRange((payload["entries"] as JArray ?? []).Where(item => item?.Value<string>(".tag") == "file")
                .Select(item => item?.Value<string>("name")).Where(value => !string.IsNullOrWhiteSpace(value)).Select(value => value!));
            cursor = payload.Value<string>("cursor");
            more = payload.Value<bool?>("has_more") ?? false;
        } while (more && !string.IsNullOrWhiteSpace(cursor));
        return names;
    }

    public async Task<string?> DownloadAsync(string fileName, bool optional, CancellationToken cancellationToken)
    {
        using HttpRequestMessage request = Request(HttpMethod.Post, "https://content.dropboxapi.com/2/files/download");
        request.Headers.TryAddWithoutValidation("Dropbox-API-Arg", JsonConvert.SerializeObject(new { path = BuildPath(fileName) }));
        using HttpResponseMessage response = await _http.SendAsync(request, cancellationToken);
        if (optional && response.StatusCode == HttpStatusCode.Conflict) return null;
        await EnsureSuccess(response, "download " + fileName, cancellationToken);
        return await response.Content.ReadAsStringAsync(cancellationToken);
    }

    public async Task UploadAsync(string fileName, string contents, CancellationToken cancellationToken)
    {
        using HttpRequestMessage request = Request(HttpMethod.Post, "https://content.dropboxapi.com/2/files/upload");
        request.Headers.TryAddWithoutValidation("Dropbox-API-Arg", JsonConvert.SerializeObject(new
        {
            path = BuildPath(fileName), mode = "overwrite", autorename = false, mute = true
        }));
        request.Content = new StringContent(contents, Encoding.UTF8, "application/octet-stream");
        using HttpResponseMessage response = await _http.SendAsync(request, cancellationToken);
        await EnsureSuccess(response, "upload " + fileName, cancellationToken);
    }

    public void Dispose() => _http.Dispose();
    private HttpRequestMessage Request(HttpMethod method, string url)
    {
        HttpRequestMessage request = new(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _token);
        return request;
    }
    private string BuildPath(string fileName) => _path + "/" + fileName;
    private static async Task EnsureSuccess(HttpResponseMessage response, string action, CancellationToken token)
    {
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Failed to {action}: HTTP {(int)response.StatusCode}. {await response.Content.ReadAsStringAsync(token)}");
    }
    private static string FormatBytes(long bytes)
    {
        double value = bytes; string[] units = ["B", "KB", "MB", "GB", "TB"]; int unit = 0;
        while (value >= 1024 && unit < units.Length - 1) { value /= 1024; unit++; }
        return $"{value:0.#} {units[unit]}";
    }
}
