using Newtonsoft.Json;
using System;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Steam_Desktop_Authenticator
{
    internal static class PairingRelayProfile
    {
        public const string ManagedEndpoint = "https://sdaplusplus-pairing-relay.eeswrtdtg4t5.workers.dev";
        public const int ClientProtocolVersion = 1;
        private static readonly HttpClient Client = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };

        public static string NormalizeEndpoint(string value)
        {
            string normalized = value?.Trim().TrimEnd('/') ?? string.Empty;
            if (!Uri.TryCreate(normalized, UriKind.Absolute, out Uri uri) ||
                uri.Scheme != Uri.UriSchemeHttps || string.IsNullOrWhiteSpace(uri.Host) ||
                !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment) ||
                (uri.AbsolutePath != "/" && !string.IsNullOrEmpty(uri.AbsolutePath)))
            {
                throw new InvalidOperationException(Localizer.Choose(
                    "Enter the HTTPS root URL of a compatible SDA++ Worker.",
                    "Введите корневой HTTPS URL совместимого SDA++ Worker."));
            }

            return normalized;
        }

        public static async Task VerifyAsync(string endpoint, CancellationToken cancellationToken)
        {
            await VerifyCoreAsync(endpoint, cancellationToken, false);
        }

        public static async Task VerifyRegistryAsync(string endpoint, CancellationToken cancellationToken)
        {
            await VerifyCoreAsync(endpoint, cancellationToken, true);
        }

        private static async Task VerifyCoreAsync(string endpoint, CancellationToken cancellationToken, bool requireRegistry)
        {
            string normalized = NormalizeEndpoint(endpoint);
            using HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, normalized + "/health");
            using HttpResponseMessage response = await Client.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException(Localizer.Choose(
                    $"Personal Worker health check returned HTTP {(int)response.StatusCode}.",
                    $"Проверка личного Worker вернула HTTP {(int)response.StatusCode}."));
            }

            string json = await response.Content.ReadAsStringAsync(cancellationToken);
            RelayHealth health;
            try
            {
                health = JsonConvert.DeserializeObject<RelayHealth>(json);
            }
            catch (JsonException)
            {
                throw new InvalidOperationException(Localizer.Choose(
                    "The endpoint is not an SDA++ relay.",
                    "Этот endpoint не является SDA++ relay."));
            }

            if (health?.Ok != true || health.Protocol != "sdapp-relay" ||
                health.ProtocolVersion < ClientProtocolVersion ||
                health.MinimumClientProtocol > ClientProtocolVersion ||
                health.Storage != "opaque-ciphertext-only" ||
                health.Capabilities?.Contains("one-time-pairing", StringComparer.Ordinal) != true ||
                (requireRegistry &&
                    (health.Capabilities?.Contains("device-registry-v1", StringComparer.Ordinal) != true ||
                     health.Capabilities?.Contains("device-encryption-v1", StringComparer.Ordinal) != true)))
            {
                throw new InvalidOperationException(Localizer.Choose(
                    "The Worker uses an incompatible SDA++ relay protocol.",
                    "Worker использует несовместимый протокол SDA++ relay."));
            }
        }

        private sealed class RelayHealth
        {
            [JsonProperty("ok")] public bool Ok { get; set; }
            [JsonProperty("protocol")] public string Protocol { get; set; }
            [JsonProperty("protocolVersion")] public int ProtocolVersion { get; set; }
            [JsonProperty("minimumClientProtocol")] public int MinimumClientProtocol { get; set; }
            [JsonProperty("capabilities")] public string[] Capabilities { get; set; }
            [JsonProperty("storage")] public string Storage { get; set; }
        }
    }
}
