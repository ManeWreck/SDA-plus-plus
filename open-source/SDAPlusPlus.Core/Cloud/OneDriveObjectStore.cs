using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.Identity.Client;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace SDAPlusPlus.Core.Cloud;

internal sealed class OneDriveObjectStore : ICloudObjectStore, IInteractiveCloudObjectStore
{
    private static readonly string[] Scopes = ["Files.ReadWrite.AppFolder"];
    private readonly HttpClient _http = new();
    private readonly IPublicClientApplication _app;
    private readonly CloudSecretStore _secrets;
    private readonly string _clientId;
    private readonly string _path;
    private Action<string>? _progress;

    public OneDriveObjectStore(CloudConnectionSettings settings, CloudSecretStore secrets)
    {
        if (string.IsNullOrWhiteSpace(settings.ClientId)) throw new InvalidOperationException("OneDrive Client ID is required.");
        _clientId = settings.ClientId.Trim();
        _path = Normalize(settings.RemotePath);
        _secrets = secrets;
        _app = PublicClientApplicationBuilder.Create(_clientId).WithAuthority(AadAuthorityAudience.PersonalMicrosoftAccount).WithDefaultRedirectUri().Build();
        ConfigureCache();
    }

    public async Task<string> SignInAsync(Action<string>? progress, CancellationToken cancellationToken)
    {
        _progress = progress;
        foreach (IAccount account in await _app.GetAccountsAsync()) await _app.RemoveAsync(account);
        AuthenticationResult result = await _app.AcquireTokenWithDeviceCode(Scopes, ShowCode).ExecuteAsync(cancellationToken);
        return "OneDrive connected as " + (result.Account?.Username ?? "Microsoft account") + ".";
    }

    public async Task<string> TestAsync(CancellationToken cancellationToken)
    {
        string token = await AcquireToken(cancellationToken);
        using HttpResponseMessage response = await Send(HttpMethod.Get, "https://graph.microsoft.com/v1.0/me/drive?$select=quota", token, null, cancellationToken);
        JObject json = JObject.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        long total = json["quota"]?.Value<long?>("total") ?? 0;
        long used = json["quota"]?.Value<long?>("used") ?? 0;
        return total > 0 ? $"OneDrive connected. Used {Format(used)} of {Format(total)}." : "OneDrive connected.";
    }

    public async Task EnsureContainerAsync(CancellationToken cancellationToken) =>
        await EnsureFolder(await AcquireToken(cancellationToken), cancellationToken);

    public async Task<IReadOnlyList<string>> ListAsync(CancellationToken cancellationToken)
    {
        string token = await AcquireToken(cancellationToken);
        string url = string.IsNullOrEmpty(_path)
            ? "https://graph.microsoft.com/v1.0/me/drive/special/approot/children?$select=name,file"
            : $"https://graph.microsoft.com/v1.0/me/drive/special/approot:/{Escape(_path)}:/children?$select=name,file";
        using HttpResponseMessage response = await Send(HttpMethod.Get, url, token, null, cancellationToken, optionalNotFound: true);
        if (response.StatusCode == HttpStatusCode.NotFound) return [];
        JObject json = JObject.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        return (json["value"] as JArray ?? []).Where(item => item?["file"] is not null)
            .Select(item => item?.Value<string>("name")).Where(item => !string.IsNullOrWhiteSpace(item)).Select(item => item!).ToArray();
    }

    public async Task<string?> DownloadAsync(string fileName, bool optional, CancellationToken cancellationToken)
    {
        string token = await AcquireToken(cancellationToken);
        using HttpResponseMessage response = await Send(HttpMethod.Get, ContentUrl(fileName), token, null, cancellationToken, optional);
        return optional && response.StatusCode == HttpStatusCode.NotFound ? null : await response.Content.ReadAsStringAsync(cancellationToken);
    }

    public async Task UploadAsync(string fileName, string contents, CancellationToken cancellationToken)
    {
        string token = await AcquireToken(cancellationToken);
        await EnsureFolder(token, cancellationToken);
        using StringContent content = new(contents, Encoding.UTF8, "application/json");
        using HttpResponseMessage _ = await Send(HttpMethod.Put, ContentUrl(fileName), token, content, cancellationToken);
    }

    private async Task<string> AcquireToken(CancellationToken cancellationToken)
    {
        IAccount? account = (await _app.GetAccountsAsync()).FirstOrDefault();
        if (account is null) throw new InvalidOperationException("OneDrive is not connected yet. Click Connect account first.");
        try { return (await _app.AcquireTokenSilent(Scopes, account).ExecuteAsync(cancellationToken)).AccessToken; }
        catch (MsalUiRequiredException) { throw new InvalidOperationException("OneDrive authorization expired. Connect the account again."); }
    }

    private Task ShowCode(DeviceCodeResult code)
    {
        _progress?.Invoke(code.Message);
        try { Process.Start(new ProcessStartInfo(code.VerificationUrl) { UseShellExecute = true }); } catch { }
        return Task.CompletedTask;
    }

    private void ConfigureCache()
    {
        _app.UserTokenCache.SetBeforeAccess(args =>
        {
            byte[] cache = _secrets.LoadBytes("onedrive-token-cache-" + _clientId);
            try { if (cache.Length > 0) args.TokenCache.DeserializeMsalV3(cache); }
            finally { if (cache.Length > 0) System.Security.Cryptography.CryptographicOperations.ZeroMemory(cache); }
        });
        _app.UserTokenCache.SetAfterAccess(args =>
        {
            if (!args.HasStateChanged) return;
            byte[] cache = args.TokenCache.SerializeMsalV3();
            try { _secrets.SaveBytes("onedrive-token-cache-" + _clientId, cache); }
            finally { System.Security.Cryptography.CryptographicOperations.ZeroMemory(cache); }
        });
    }

    private async Task EnsureFolder(string token, CancellationToken cancellationToken)
    {
        string parent = string.Empty;
        foreach (string segment in _path.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            string current = string.IsNullOrEmpty(parent) ? segment : parent + "/" + segment;
            using HttpResponseMessage probe = await Send(HttpMethod.Get, $"https://graph.microsoft.com/v1.0/me/drive/special/approot:/{Escape(current)}", token, null, cancellationToken, true);
            if (probe.StatusCode == HttpStatusCode.NotFound)
            {
                string url = string.IsNullOrEmpty(parent) ? "https://graph.microsoft.com/v1.0/me/drive/special/approot/children" : $"https://graph.microsoft.com/v1.0/me/drive/special/approot:/{Escape(parent)}:/children";
                using StringContent body = new(new JObject
                {
                    ["name"] = segment,
                    ["folder"] = new JObject(),
                    ["@microsoft.graph.conflictBehavior"] = "fail"
                }.ToString(Formatting.None), Encoding.UTF8, "application/json");
                using HttpResponseMessage _ = await Send(HttpMethod.Post, url, token, body, cancellationToken);
            }
            parent = current;
        }
    }

    private async Task<HttpResponseMessage> Send(HttpMethod method, string url, string token, HttpContent? content, CancellationToken cancellationToken, bool optionalNotFound = false)
    {
        using HttpRequestMessage request = new(method, url) { Content = content };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        HttpResponseMessage response = await _http.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode && !(optionalNotFound && response.StatusCode == HttpStatusCode.NotFound))
        {
            string detail = await response.Content.ReadAsStringAsync(cancellationToken); response.Dispose();
            throw new InvalidOperationException($"OneDrive returned HTTP {(int)response.StatusCode}. {detail}");
        }
        return response;
    }

    private string ContentUrl(string file) => $"https://graph.microsoft.com/v1.0/me/drive/special/approot:/{Escape(string.IsNullOrEmpty(_path) ? file : _path + "/" + file)}:/content";
    private static string Normalize(string value) => (value ?? string.Empty).Trim().Trim('/');
    private static string Escape(string value) => string.Join('/', Normalize(value).Split('/').Select(Uri.EscapeDataString));
    private static string Format(long bytes) { double v = bytes; string[] u = ["B", "KB", "MB", "GB", "TB"]; int i = 0; while (v >= 1024 && i < 4) { v /= 1024; i++; } return $"{v:0.#} {u[i]}"; }
    public void Dispose() => _http.Dispose();
}
