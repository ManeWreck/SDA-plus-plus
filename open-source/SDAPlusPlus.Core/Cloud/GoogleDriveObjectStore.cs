using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace SDAPlusPlus.Core.Cloud;

internal sealed class GoogleDriveObjectStore : ICloudObjectStore, IInteractiveCloudObjectStore
{
    private const string FolderMime = "application/vnd.google-apps.folder";
    private const string Scope = "https://www.googleapis.com/auth/drive.file https://www.googleapis.com/auth/drive.metadata.readonly";
    private readonly HttpClient _http = new();
    private readonly CloudSecretStore _secrets;
    private readonly string _clientId;
    private readonly string _path;
    private string? _folderId;

    public GoogleDriveObjectStore(CloudConnectionSettings settings, CloudSecretStore secrets)
    {
        if (string.IsNullOrWhiteSpace(settings.ClientId)) throw new InvalidOperationException("Google OAuth Client ID is required.");
        _clientId = settings.ClientId.Trim(); _path = (settings.RemotePath ?? string.Empty).Trim().Trim('/'); _secrets = secrets;
    }

    public async Task<string> SignInAsync(Action<string>? progress, CancellationToken cancellationToken)
    {
        string verifier = Encode(RandomNumberGenerator.GetBytes(64));
        string challenge = Encode(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        int port = FreePort(); string redirect = $"http://127.0.0.1:{port}/"; string state = Guid.NewGuid().ToString("N");
        string url = "https://accounts.google.com/o/oauth2/v2/auth?client_id=" + Uri.EscapeDataString(_clientId) +
            "&redirect_uri=" + Uri.EscapeDataString(redirect) + "&response_type=code&scope=" + Uri.EscapeDataString(Scope) +
            "&access_type=offline&prompt=consent&code_challenge=" + challenge + "&code_challenge_method=S256&state=" + state;
        using HttpListener listener = new(); listener.Prefixes.Add(redirect); listener.Start();
        progress?.Invoke("Complete Google authorization in the browser.");
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        HttpListenerContext context = await listener.GetContextAsync().WaitAsync(cancellationToken);
        string? code = context.Request.QueryString["code"]; string? returnedState = context.Request.QueryString["state"]; string? error = context.Request.QueryString["error"];
        await Reply(context.Response, string.IsNullOrWhiteSpace(error) ? "Google Drive connected. You can return to SDA++." : "Google Drive sign-in failed.");
        if (!string.IsNullOrWhiteSpace(error)) throw new InvalidOperationException("Google sign-in failed: " + error);
        if (state != returnedState || string.IsNullOrWhiteSpace(code)) throw new InvalidOperationException("Google sign-in state verification failed.");
        using FormUrlEncodedContent body = new(new Dictionary<string, string>
        {
            ["code"] = code, ["client_id"] = _clientId, ["redirect_uri"] = redirect, ["grant_type"] = "authorization_code", ["code_verifier"] = verifier
        });
        using HttpResponseMessage response = await _http.PostAsync("https://oauth2.googleapis.com/token", body, cancellationToken);
        await Ensure(response, "exchange Google authorization code", cancellationToken);
        TokenState token = ParseToken(await response.Content.ReadAsStringAsync(cancellationToken), null); Save(token);
        return "Google Drive connected.";
    }

    public async Task<string> TestAsync(CancellationToken cancellationToken)
    {
        string token = await Token(cancellationToken);
        using HttpResponseMessage response = await Send(HttpMethod.Get, "https://www.googleapis.com/drive/v3/about?fields=user(displayName,emailAddress),storageQuota(limit,usage)", token, null, cancellationToken);
        JObject json = JObject.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        string owner = json["user"]?.Value<string>("displayName") ?? json["user"]?.Value<string>("emailAddress") ?? "Google account";
        long limit = json["storageQuota"]?.Value<long?>("limit") ?? 0; long used = json["storageQuota"]?.Value<long?>("usage") ?? 0;
        return limit > 0 ? $"Google Drive connected as {owner}. Used {Format(used)} of {Format(limit)}." : $"Google Drive connected as {owner}.";
    }

    public async Task EnsureContainerAsync(CancellationToken cancellationToken) =>
        _ = await Folder(await Token(cancellationToken), true, cancellationToken);

    public async Task<IReadOnlyList<string>> ListAsync(CancellationToken cancellationToken)
    {
        string token = await Token(cancellationToken); string? parent = await Folder(token, false, cancellationToken); if (parent is null) return [];
        string query = $"trashed = false and '{parent}' in parents";
        using HttpResponseMessage response = await Send(HttpMethod.Get, "https://www.googleapis.com/drive/v3/files?q=" + Uri.EscapeDataString(query) + "&fields=files(id,name,mimeType)&pageSize=500", token, null, cancellationToken);
        JObject json = JObject.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        return (json["files"] as JArray ?? []).Where(item => item?.Value<string>("mimeType") != FolderMime)
            .Select(item => item?.Value<string>("name")).Where(item => !string.IsNullOrWhiteSpace(item)).Select(item => item!).ToArray();
    }

    public async Task<string?> DownloadAsync(string fileName, bool optional, CancellationToken cancellationToken)
    {
        string token = await Token(cancellationToken); string? parent = await Folder(token, false, cancellationToken);
        if (parent is null) return optional ? null : throw new FileNotFoundException(fileName);
        string? id = await Find(token, parent, fileName, null, cancellationToken);
        if (id is null) return optional ? null : throw new FileNotFoundException(fileName);
        using HttpResponseMessage response = await Send(HttpMethod.Get, $"https://www.googleapis.com/drive/v3/files/{id}?alt=media", token, null, cancellationToken);
        return await response.Content.ReadAsStringAsync(cancellationToken);
    }

    public async Task UploadAsync(string fileName, string contents, CancellationToken cancellationToken)
    {
        string token = await Token(cancellationToken); string parent = (await Folder(token, true, cancellationToken))!;
        string? id = await Find(token, parent, fileName, null, cancellationToken);
        string url = id is null ? "https://www.googleapis.com/upload/drive/v3/files?uploadType=multipart" : $"https://www.googleapis.com/upload/drive/v3/files/{id}?uploadType=multipart";
        using MultipartContent multipart = new("related", "sda-" + Guid.NewGuid().ToString("N"));
        object metadata = id is null ? new { name = fileName, parents = new[] { parent } } : new { name = fileName };
        multipart.Add(new StringContent(JsonConvert.SerializeObject(metadata), Encoding.UTF8, "application/json"));
        multipart.Add(new StringContent(contents, Encoding.UTF8, "application/json"));
        using HttpResponseMessage _ = await Send(id is null ? HttpMethod.Post : new HttpMethod("PATCH"), url, token, multipart, cancellationToken);
    }

    private async Task<string> Token(CancellationToken cancellationToken)
    {
        TokenState state = Load();
        if (!string.IsNullOrWhiteSpace(state.AccessToken) && state.Expires > DateTimeOffset.UtcNow.AddMinutes(1)) return state.AccessToken;
        if (string.IsNullOrWhiteSpace(state.RefreshToken)) throw new InvalidOperationException("Google Drive is not connected yet. Click Connect account first.");
        using FormUrlEncodedContent body = new(new Dictionary<string, string> { ["client_id"] = _clientId, ["refresh_token"] = state.RefreshToken, ["grant_type"] = "refresh_token" });
        using HttpResponseMessage response = await _http.PostAsync("https://oauth2.googleapis.com/token", body, cancellationToken); await Ensure(response, "refresh Google Drive token", cancellationToken);
        TokenState refreshed = ParseToken(await response.Content.ReadAsStringAsync(cancellationToken), state.RefreshToken); Save(refreshed); return refreshed.AccessToken;
    }

    private async Task<string?> Folder(string token, bool create, CancellationToken cancellationToken)
    {
        if (_folderId is not null) return _folderId; string parent = "root";
        foreach (string segment in _path.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            string? id = await Find(token, parent, segment, FolderMime, cancellationToken);
            if (id is null && create)
            {
                using StringContent body = new(JsonConvert.SerializeObject(new { name = segment, mimeType = FolderMime, parents = new[] { parent } }), Encoding.UTF8, "application/json");
                using HttpResponseMessage response = await Send(HttpMethod.Post, "https://www.googleapis.com/drive/v3/files?fields=id", token, body, cancellationToken);
                id = JObject.Parse(await response.Content.ReadAsStringAsync(cancellationToken)).Value<string>("id");
            }
            if (id is null) return null; parent = id;
        }
        return _folderId = parent;
    }

    private async Task<string?> Find(string token, string parent, string name, string? mime, CancellationToken cancellationToken)
    {
        string query = $"name = '{name.Replace("'", "\\'")}' and trashed = false and '{parent}' in parents" + (mime is null ? "" : $" and mimeType = '{mime}'");
        using HttpResponseMessage response = await Send(HttpMethod.Get, "https://www.googleapis.com/drive/v3/files?q=" + Uri.EscapeDataString(query) + "&fields=files(id)&pageSize=1", token, null, cancellationToken);
        return (JObject.Parse(await response.Content.ReadAsStringAsync(cancellationToken))["files"] as JArray)?.FirstOrDefault()?.Value<string>("id");
    }

    private async Task<HttpResponseMessage> Send(HttpMethod method, string url, string token, HttpContent? content, CancellationToken cancellationToken)
    {
        using HttpRequestMessage request = new(method, url) { Content = content }; request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        HttpResponseMessage response = await _http.SendAsync(request, cancellationToken); await Ensure(response, "access Google Drive", cancellationToken); return response;
    }
    private static async Task Ensure(HttpResponseMessage response, string action, CancellationToken token) { if (!response.IsSuccessStatusCode) throw new InvalidOperationException($"Failed to {action}: HTTP {(int)response.StatusCode}. {await response.Content.ReadAsStringAsync(token)}"); }
    private TokenState Load() { string json = _secrets.Load("gdrive-token-" + _clientId); return string.IsNullOrWhiteSpace(json) ? new() : JsonConvert.DeserializeObject<TokenState>(json) ?? new(); }
    private void Save(TokenState state) => _secrets.Save("gdrive-token-" + _clientId, JsonConvert.SerializeObject(state));
    private static TokenState ParseToken(string json, string? oldRefresh) { JObject value = JObject.Parse(json); string access = value.Value<string>("access_token") ?? throw new InvalidOperationException("Google returned no access token."); return new() { AccessToken = access, RefreshToken = value.Value<string>("refresh_token") ?? oldRefresh ?? "", Expires = DateTimeOffset.UtcNow.AddSeconds(Math.Max(60, value.Value<int?>("expires_in") ?? 3600)) }; }
    private static string Encode(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    private static int FreePort() { TcpListener listener = new(IPAddress.Loopback, 0); listener.Start(); int port = ((IPEndPoint)listener.LocalEndpoint).Port; listener.Stop(); return port; }
    private static async Task Reply(HttpListenerResponse response, string message) { byte[] data = Encoding.UTF8.GetBytes("<html><body>" + WebUtility.HtmlEncode(message) + "</body></html>"); response.ContentLength64 = data.Length; await response.OutputStream.WriteAsync(data); response.Close(); }
    private static string Format(long bytes) { double v = bytes; string[] u = ["B", "KB", "MB", "GB", "TB"]; int i = 0; while (v >= 1024 && i < 4) { v /= 1024; i++; } return $"{v:0.#} {u[i]}"; }
    public void Dispose() => _http.Dispose();
    private sealed class TokenState { public string AccessToken { get; set; } = ""; public string RefreshToken { get; set; } = ""; public DateTimeOffset Expires { get; set; } }
}
