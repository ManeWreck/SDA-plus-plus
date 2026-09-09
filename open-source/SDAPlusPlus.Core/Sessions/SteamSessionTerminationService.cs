using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
using ProtoBuf;
using SteamAuth;
using SteamKit2.Internal;

namespace SDAPlusPlus.Core.Sessions;

public sealed partial class SteamSessionTerminationService : ISteamSessionTerminationService
{
    private const string ManageUrl = "https://store.steampowered.com/twofactor/manage";
    private const string ManageActionUrl = "https://store.steampowered.com/twofactor/manage_action";
    private const string FinalizeLoginUrl = "https://login.steampowered.com/jwt/finalizelogin";
    private const string EnumerateTokensUrl = "https://api.steampowered.com/IAuthenticationService/EnumerateTokens/v1/";
    private const string BrowserUserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 SDAPlusPlus/1.7";

    public async Task<SteamSessionListResult> GetActiveSessionsAsync(
        SteamGuardAccount account,
        CancellationToken cancellationToken = default)
    {
        if (account?.Session is null ||
            (string.IsNullOrWhiteSpace(account.Session.AccessToken) && string.IsNullOrWhiteSpace(account.Session.RefreshToken)))
        {
            return new SteamSessionListResult(SteamSessionListState.MissingSession);
        }

        try
        {
            if (!await EnsureAccessTokenAsync(account.Session, cancellationToken).ConfigureAwait(false))
            {
                return new SteamSessionListResult(SteamSessionListState.ExpiredSession);
            }

            byte[] requestPayload;
            using (var stream = new MemoryStream())
            {
                Serializer.Serialize(stream, new CAuthentication_RefreshToken_Enumerate_Request());
                requestPayload = stream.ToArray();
            }

            using HttpClient client = CreateApiClient();
            using HttpResponseMessage response = await SendProtobufAsync(
                client,
                EnumerateTokensUrl,
                account.Session.AccessToken,
                requestPayload,
                cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return new SteamSessionListResult(SteamSessionListState.SteamRejected, Detail: ReadSteamError(response));
            }

            byte[] content = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
            IReadOnlyList<SteamActiveSession> sessions = ParseSessions(content);
            return new SteamSessionListResult(SteamSessionListState.Succeeded, sessions);
        }
        catch (OperationCanceledException)
        {
            return new SteamSessionListResult(SteamSessionListState.Cancelled);
        }
        catch (Exception exception)
        {
            return new SteamSessionListResult(
                SteamSessionListState.AuthenticationFailed,
                Detail: SanitizeDetail(exception.Message));
        }
    }

    public async Task<SteamSessionTerminationResult> TerminateAllAsync(
        SteamGuardAccount account,
        CancellationToken cancellationToken = default)
    {
        if (account?.Session is null || string.IsNullOrWhiteSpace(account.Session.RefreshToken))
        {
            return new SteamSessionTerminationResult(SteamSessionTerminationState.MissingSession);
        }

        try
        {
            if (account.Session.IsRefreshTokenExpired())
            {
                return new SteamSessionTerminationResult(SteamSessionTerminationState.ExpiredSession);
            }

            cancellationToken.ThrowIfCancellationRequested();
            using WebSession webSession = await TryCreateDirectSessionAsync(account, cancellationToken).ConfigureAwait(false)
                ?? await CreateTransferredSessionAsync(account, cancellationToken).ConfigureAwait(false);
            if (!ContainsDeauthorizeForm(webSession.ManageHtml))
            {
                return new SteamSessionTerminationResult(
                    SteamSessionTerminationState.AuthenticationFailed,
                    "Steam did not return an authenticated Steam Guard management page.");
            }

            string sessionId = ExtractSessionId(webSession.ManageHtml) ?? webSession.SessionId;
            using var request = new HttpRequestMessage(HttpMethod.Post, ManageActionUrl)
            {
                Content = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["action"] = "deauthorize",
                    ["sessionid"] = sessionId
                })
            };
            request.Headers.Referrer = new Uri(ManageUrl);
            using HttpResponseMessage response = await webSession.Client.SendAsync(request, cancellationToken).ConfigureAwait(false);
            string html = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode || IsSignInPage(html))
            {
                return new SteamSessionTerminationResult(SteamSessionTerminationState.SteamRejected);
            }

            return new SteamSessionTerminationResult(SteamSessionTerminationState.Succeeded);
        }
        catch (OperationCanceledException)
        {
            return new SteamSessionTerminationResult(SteamSessionTerminationState.Cancelled);
        }
        catch (Exception exception) when (exception is HttpRequestException or Newtonsoft.Json.JsonException or
                                           FormatException or CryptographicException or InvalidOperationException)
        {
            return new SteamSessionTerminationResult(
                SteamSessionTerminationState.AuthenticationFailed,
                exception.Message.Length <= 300 ? exception.Message : exception.Message[..300]);
        }
    }

    private static async Task<WebSession?> TryCreateDirectSessionAsync(
        SteamGuardAccount account,
        CancellationToken cancellationToken)
    {
        CookieContainer cookies = account.Session.GetCookies();
        HttpClient client = CreateClient(cookies);
        try
        {
            string html = await client.GetStringAsync(ManageUrl, cancellationToken).ConfigureAwait(false);
            if (!ContainsDeauthorizeForm(html))
            {
                client.Dispose();
                return null;
            }

            string sessionId = cookies.GetCookies(new Uri(ManageUrl))["sessionid"]?.Value
                ?? account.Session.SessionID
                ?? ExtractSessionId(html)
                ?? GenerateSessionId();
            return new WebSession(client, sessionId, html);
        }
        catch
        {
            client.Dispose();
            return null;
        }
    }

    private static async Task<WebSession> CreateTransferredSessionAsync(
        SteamGuardAccount account,
        CancellationToken cancellationToken)
    {
        string sessionId = GenerateSessionId();
        var cookies = new CookieContainer();
        HttpClient client = CreateClient(cookies);
        try
        {
            using var finalizeRequest = new HttpRequestMessage(HttpMethod.Post, FinalizeLoginUrl)
            {
                Content = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["nonce"] = account.Session.RefreshToken,
                    ["sessionid"] = sessionId,
                    ["redir"] = "https://steamcommunity.com/login/home/?goto="
                })
            };
            finalizeRequest.Headers.Referrer = new Uri("https://steamcommunity.com/");
            using HttpResponseMessage finalizeResponse = await client.SendAsync(finalizeRequest, cancellationToken).ConfigureAwait(false);
            finalizeResponse.EnsureSuccessStatusCode();
            string json = await finalizeResponse.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            JObject root = JObject.Parse(json);
            JToken payload = root["response"] ?? root;
            JArray transfers = payload["transfer_info"] as JArray
                ?? throw new InvalidOperationException("Steam returned an unexpected login transfer response.");

            foreach (JToken transfer in transfers)
            {
                string url = transfer["url"]?.Value<string>()
                    ?? throw new InvalidOperationException("Steam returned an invalid transfer URL.");
                if (!IsTrustedSteamUrl(url))
                {
                    throw new InvalidOperationException("Steam returned an untrusted login transfer URL.");
                }

                var values = new Dictionary<string, string> { ["steamID"] = account.Session.SteamID.ToString() };
                if (transfer["params"] is JObject parameters)
                {
                    foreach (JProperty property in parameters.Properties())
                    {
                        values[property.Name] = property.Value.ToString();
                    }
                }

                using var transferRequest = new HttpRequestMessage(HttpMethod.Post, url)
                {
                    Content = new FormUrlEncodedContent(values)
                };
                transferRequest.Headers.Referrer = new Uri(url);
                using HttpResponseMessage transferResponse = await client.SendAsync(transferRequest, cancellationToken).ConfigureAwait(false);
                transferResponse.EnsureSuccessStatusCode();
            }

            AddSessionCookies(cookies, sessionId);
            string manageHtml = await client.GetStringAsync(ManageUrl, cancellationToken).ConfigureAwait(false);
            return new WebSession(client, sessionId, manageHtml);
        }
        catch
        {
            client.Dispose();
            throw;
        }
    }

    private static HttpClient CreateClient(CookieContainer cookies)
    {
        var handler = new HttpClientHandler
        {
            CookieContainer = cookies,
            AutomaticDecompression = DecompressionMethods.All,
            AllowAutoRedirect = true
        };
        var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(25) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd(BrowserUserAgent);
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("text/html"));
        return client;
    }

    private static HttpClient CreateApiClient()
    {
        var client = new HttpClient(new HttpClientHandler
        {
            AutomaticDecompression = DecompressionMethods.All
        })
        {
            Timeout = TimeSpan.FromSeconds(20)
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd(BrowserUserAgent);
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/x-protobuf"));
        return client;
    }

    private static async Task<bool> EnsureAccessTokenAsync(SessionData session, CancellationToken cancellationToken)
    {
        if (!session.IsAccessTokenExpired())
        {
            return true;
        }

        if (session.IsRefreshTokenExpired())
        {
            return false;
        }

        await session.RefreshAccessToken(allowRenewal: false).WaitAsync(cancellationToken).ConfigureAwait(false);
        return !session.IsAccessTokenExpired();
    }

    private static async Task<HttpResponseMessage> SendProtobufAsync(
        HttpClient client,
        string url,
        string accessToken,
        byte[] payload,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{url}?access_token={Uri.EscapeDataString(accessToken)}");
        if (payload.Length > 0)
        {
            request.Content = new MultipartFormDataContent
            {
                { new StringContent(Convert.ToBase64String(payload)), "input_protobuf_encoded" }
            };
        }
        return await client.SendAsync(request, cancellationToken).ConfigureAwait(false);
    }

    private static SteamActiveSession MapSession(
        CAuthentication_RefreshToken_Enumerate_Response.RefreshTokenDescription token,
        ulong requestingToken)
    {
        CAuthentication_RefreshToken_Enumerate_Response.TokenUsageEvent? usage = token.last_seen ?? token.first_seen;
        string location = string.Join(", ", new[] { usage?.city, usage?.state, usage?.country }
            .Where(value => !string.IsNullOrWhiteSpace(value)));
        return new SteamActiveSession(
            token.token_id,
            string.IsNullOrWhiteSpace(token.token_description) ? PlatformLabel(token.platform_type) : token.token_description.Trim(),
            PlatformLabel(token.platform_type),
            FormatIpAddress(usage?.ip),
            location,
            ToDateTime(token.first_seen?.time),
            ToDateTime(token.last_seen?.time ?? token.time_updated),
            token.token_id == requestingToken,
            token.logged_in);
    }

    private static IReadOnlyList<SteamActiveSession> ParseSessions(byte[] content)
    {
        int first = Array.FindIndex(content, value => !char.IsWhiteSpace((char)value));
        if (first >= 0 && content[first] == (byte)'{')
        {
            return ParseJsonSessions(Encoding.UTF8.GetString(content));
        }

        using var stream = new MemoryStream(content, writable: false);
        CAuthentication_RefreshToken_Enumerate_Response payload =
            Serializer.Deserialize<CAuthentication_RefreshToken_Enumerate_Response>(stream);
        return payload.refresh_tokens
            .Where(token => token.logged_in)
            .OrderByDescending(token => token.token_id == payload.requesting_token)
            .ThenByDescending(token => token.last_seen?.time ?? token.time_updated)
            .Select(token => MapSession(token, payload.requesting_token))
            .ToArray();
    }

    private static IReadOnlyList<SteamActiveSession> ParseJsonSessions(string json)
    {
        JToken root = JObject.Parse(json);
        JToken payload = root["response"] ?? root;
        ulong requestingToken = ParseUInt64(payload["requesting_token"]);
        return (payload["refresh_tokens"] as JArray ?? [])
            .Where(token => token["logged_in"]?.Value<bool>() == true)
            .Select(token => MapJsonSession(token, requestingToken))
            .OrderByDescending(session => session.IsCurrent)
            .ThenByDescending(session => session.LastSeen)
            .ToArray();
    }

    private static SteamActiveSession MapJsonSession(JToken token, ulong requestingToken)
    {
        ulong tokenId = ParseUInt64(token["token_id"]);
        var platform = (EAuthTokenPlatformType)(token["platform_type"]?.Value<int>() ?? 0);
        JToken? firstSeen = token["first_seen"];
        JToken? lastSeen = token["last_seen"];
        JToken? usage = lastSeen ?? firstSeen;
        string location = string.Join(", ", new[]
        {
            usage?["city"]?.Value<string>(),
            usage?["state"]?.Value<string>(),
            usage?["country"]?.Value<string>()
        }.Where(value => !string.IsNullOrWhiteSpace(value)));
        return new SteamActiveSession(
            tokenId,
            string.IsNullOrWhiteSpace(token["token_description"]?.Value<string>())
                ? PlatformLabel(platform)
                : token["token_description"]!.Value<string>()!.Trim(),
            PlatformLabel(platform),
            FormatJsonIpAddress(usage?["ip"]),
            location,
            ToDateTime(firstSeen?["time"]?.Value<uint?>()),
            ToDateTime(lastSeen?["time"]?.Value<uint?>() ?? token["time_updated"]?.Value<uint?>()),
            tokenId == requestingToken,
            token["logged_in"]?.Value<bool>() == true);
    }

    private static string FormatJsonIpAddress(JToken? address)
    {
        if (address is null)
        {
            return string.Empty;
        }

        JToken? v6 = address["v6"];
        if (v6?.Type == JTokenType.String)
        {
            byte[] bytes = Convert.FromBase64String(v6.Value<string>()!);
            if (bytes.Length == 16)
            {
                return new IPAddress(bytes).ToString();
            }
        }

        uint v4 = ParseUInt32(address["v4"]);
        if (v4 == 0)
        {
            return string.Empty;
        }

        byte[] v4Bytes = BitConverter.GetBytes(v4);
        Array.Reverse(v4Bytes);
        return new IPAddress(v4Bytes).ToString();
    }

    private static ulong ParseUInt64(JToken? value) =>
        ulong.TryParse(value?.ToString(), out ulong parsed) ? parsed : 0;

    private static uint ParseUInt32(JToken? value) =>
        uint.TryParse(value?.ToString(), out uint parsed) ? parsed : 0;

    private static string PlatformLabel(EAuthTokenPlatformType platform) => platform switch
    {
        EAuthTokenPlatformType.k_EAuthTokenPlatformType_MobileApp => "Mobile",
        EAuthTokenPlatformType.k_EAuthTokenPlatformType_SteamClient => "Steam Client",
        EAuthTokenPlatformType.k_EAuthTokenPlatformType_WebBrowser => "Browser",
        _ => "Unknown device"
    };

    private static string FormatIpAddress(CMsgIPAddress? address)
    {
        if (address is null)
        {
            return string.Empty;
        }

        if (address.ShouldSerializev6() && address.v6 is { Length: 16 })
        {
            return new IPAddress(address.v6).ToString();
        }

        if (address.ShouldSerializev4())
        {
            byte[] bytes = BitConverter.GetBytes(address.v4);
            Array.Reverse(bytes);
            return new IPAddress(bytes).ToString();
        }

        return string.Empty;
    }

    private static DateTimeOffset? ToDateTime(uint? unixTime) => unixTime is > 0
        ? DateTimeOffset.FromUnixTimeSeconds(unixTime.Value)
        : null;

    private static string? ReadSteamError(HttpResponseMessage response)
    {
        string? message = response.Headers.TryGetValues("x-error_message", out IEnumerable<string>? values)
            ? values.FirstOrDefault()
            : null;
        return string.IsNullOrWhiteSpace(message)
            ? $"Steam returned HTTP {(int)response.StatusCode}."
            : SanitizeDetail(message);
    }

    private static string SanitizeDetail(string message) => message.Length <= 300 ? message : message[..300];

    private static void AddSessionCookies(CookieContainer cookies, string sessionId)
    {
        foreach (string domain in new[] { "steamcommunity.com", "store.steampowered.com", "help.steampowered.com", "checkout.steampowered.com", "steam.tv" })
        {
            cookies.Add(new Cookie("sessionid", sessionId, "/", domain));
        }
    }

    private static bool ContainsDeauthorizeForm(string? html) =>
        !string.IsNullOrWhiteSpace(html) &&
        (html.Contains("deauthorize_devices_form", StringComparison.OrdinalIgnoreCase) ||
         html.Contains("manage_action", StringComparison.OrdinalIgnoreCase));

    private static bool IsSignInPage(string? html) =>
        !string.IsNullOrWhiteSpace(html) &&
        (html.Contains("loginForm", StringComparison.OrdinalIgnoreCase) ||
         html.Contains("/login/home", StringComparison.OrdinalIgnoreCase));

    private static string? ExtractSessionId(string html)
    {
        Match match = DeauthorizeSessionIdRegex().Match(html);
        return match.Success ? WebUtility.HtmlDecode(match.Groups["sessionid"].Value) : null;
    }

    private static string GenerateSessionId() => Convert.ToHexString(RandomNumberGenerator.GetBytes(12)).ToLowerInvariant();

    private static bool IsTrustedSteamUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri) || uri.Scheme != Uri.UriSchemeHttps)
        {
            return false;
        }

        return uri.Host.Equals("steampowered.com", StringComparison.OrdinalIgnoreCase) ||
               uri.Host.EndsWith(".steampowered.com", StringComparison.OrdinalIgnoreCase) ||
               uri.Host.Equals("steamcommunity.com", StringComparison.OrdinalIgnoreCase) ||
               uri.Host.EndsWith(".steamcommunity.com", StringComparison.OrdinalIgnoreCase);
    }

    [GeneratedRegex("<form[^>]*id=\\\"deauthorize_devices_form\\\"[\\s\\S]*?<input[^>]*name=\\\"sessionid\\\"[^>]*value=\\\"(?<sessionid>[^\\\"]+)\\\"", RegexOptions.IgnoreCase)]
    private static partial Regex DeauthorizeSessionIdRegex();

    private sealed record WebSession(HttpClient Client, string SessionId, string ManageHtml) : IDisposable
    {
        public void Dispose() => Client.Dispose();
    }
}
