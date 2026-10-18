using Newtonsoft.Json.Linq;

namespace SDAPlusPlus.Core.Devices;

public sealed class PairingRelayService(HttpClient httpClient) : IPairingRelayService
{
    public const string ManagedEndpoint = "https://sdaplusplus-pairing-relay.eeswrtdtg4t5.workers.dev";

    public async Task<PairingRelayHealthResult> CheckHealthAsync(
        string endpoint,
        CancellationToken cancellationToken = default)
    {
        string normalized = NormalizeEndpoint(endpoint);
        try
        {
            using HttpResponseMessage response = await httpClient.GetAsync(normalized + "/health", cancellationToken);
            string text = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
                return new PairingRelayHealthResult(false, normalized, null, 0, $"HTTP {(int)response.StatusCode}");

            JObject json = JObject.Parse(text);
            int protocolVersion = json.Value<int?>("protocolVersion") ?? 0;
            int minimumClientProtocol = json.Value<int?>("minimumClientProtocol") ?? 1;
            string? storage = json.Value<string>("storage");
            HashSet<string> capabilities = json["capabilities"]?.Values<string>()
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value!)
                .ToHashSet(StringComparer.Ordinal) ?? [];
            bool compatible = json.Value<bool?>("ok") == true &&
                string.Equals(json.Value<string>("protocol"), "sdapp-relay", StringComparison.Ordinal) &&
                protocolVersion >= 1 && minimumClientProtocol <= 1 &&
                string.Equals(storage, "opaque-ciphertext-only", StringComparison.Ordinal) &&
                capabilities.IsSupersetOf(["one-time-pairing", "device-registry-v1", "device-encryption-v1", "signed-action-grants-v1"]);

            return new PairingRelayHealthResult(
                compatible,
                normalized,
                storage,
                protocolVersion,
                compatible ? null : "The Worker uses an incompatible SDA++ relay protocol.");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or Newtonsoft.Json.JsonException)
        {
            return new PairingRelayHealthResult(false, normalized, null, 0, ex.Message);
        }
    }

    private static string NormalizeEndpoint(string endpoint)
    {
        if (!Uri.TryCreate(endpoint?.Trim(), UriKind.Absolute, out Uri? uri) ||
            uri.Scheme != Uri.UriSchemeHttps || !string.IsNullOrEmpty(uri.Query) ||
            !string.IsNullOrEmpty(uri.Fragment) || (uri.AbsolutePath != "/" && uri.AbsolutePath.Length > 0))
            throw new ArgumentException("Pairing relay must be an HTTPS root URL.", nameof(endpoint));

        return uri.GetLeftPart(UriPartial.Authority).TrimEnd('/');
    }
}
