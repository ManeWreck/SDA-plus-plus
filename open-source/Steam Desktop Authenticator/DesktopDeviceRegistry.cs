using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Steam_Desktop_Authenticator
{
    internal sealed class DesktopRegistrySettings
    {
        public string VaultId { get; set; } = string.Empty;
        public string RelayUrl { get; set; } = string.Empty;
        public string Role { get; set; } = string.Empty;
    }

    internal sealed class DesktopRegistryDevice
    {
        [JsonProperty("deviceId")] public string DeviceId { get; set; }
        [JsonProperty("displayName")] public string DisplayName { get; set; }
        [JsonProperty("role")] public string Role { get; set; }
        [JsonProperty("lastSeenAt")] public long LastSeenAt { get; set; }
        [JsonProperty("revokedAt")] public long? RevokedAt { get; set; }
    }

    internal sealed class DesktopDeviceInvitation
    {
        public string QrPayload { get; init; }
        public long ExpiresAt { get; init; }
    }

    internal static class DesktopRegistrySettingsStore
    {
        private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("SDA++ desktop device registry v1");
        private static string FilePath => Path.Combine(Manifest.GetExecutableDir(), "maFiles", "device-registry.secure");

        public static DesktopRegistrySettings Load()
        {
            if (!File.Exists(FilePath)) return new DesktopRegistrySettings();
            byte[] encrypted = File.ReadAllBytes(FilePath);
            byte[] plain = ProtectedData.Unprotect(encrypted, Entropy, DataProtectionScope.CurrentUser);
            try { return JsonConvert.DeserializeObject<DesktopRegistrySettings>(Encoding.UTF8.GetString(plain)) ?? new DesktopRegistrySettings(); }
            finally { CryptographicOperations.ZeroMemory(plain); }
        }

        public static void Save(DesktopRegistrySettings settings)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
            byte[] plain = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(settings));
            try { File.WriteAllBytes(FilePath, ProtectedData.Protect(plain, Entropy, DataProtectionScope.CurrentUser)); }
            finally { CryptographicOperations.ZeroMemory(plain); }
        }
    }

    internal sealed class DesktopDeviceRegistryClient
    {
        private static readonly HttpClient Http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };

        public async Task<DesktopRegistrySettings> BootstrapAdminAsync(string endpoint, string displayName, CancellationToken token)
        {
            endpoint = PairingRelayProfile.NormalizeEndpoint(endpoint);
            await PairingRelayProfile.VerifyAsync(endpoint, token);
            string vaultId = RandomBase64Url(16);
            DeviceIdentityInfo identity = DeviceIdentityStore.GetInfo();
            JObject body = new JObject
            {
                ["role"] = "Admin",
                ["displayName"] = (displayName ?? Environment.MachineName).Substring(0, Math.Min((displayName ?? Environment.MachineName).Length, 64)),
                ["keyProtection"] = identity.Protection
            };
            await SendAsync(endpoint, $"/v1/trust/{vaultId}/bootstrap", HttpMethod.Put, body, true, null, token);
            DesktopRegistrySettings settings = new DesktopRegistrySettings { VaultId = vaultId, RelayUrl = endpoint, Role = "Admin" };
            DesktopRegistrySettingsStore.Save(settings);
            return settings;
        }

        public async Task<IReadOnlyList<DesktopRegistryDevice>> ListDevicesAsync(DesktopRegistrySettings settings, CancellationToken token)
        {
            JObject response = await SendAsync(settings.RelayUrl, $"/v1/trust/{settings.VaultId}/devices", HttpMethod.Get, null, false, null, token);
            return response["devices"]?.ToObject<List<DesktopRegistryDevice>>() ?? new List<DesktopRegistryDevice>();
        }

        public async Task<DesktopDeviceInvitation> CreateInvitationAsync(
            DesktopRegistrySettings settings,
            IEnumerable<string> steamIds,
            IEnumerable<string> capabilities,
            CancellationToken cancellationToken)
        {
            string inviteId = RandomBase64Url(16);
            string inviteToken = RandomBase64Url(32);
            long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            long expiresAt = now + (long)TimeSpan.FromMinutes(5).TotalMilliseconds;
            JObject grant = new JObject
            {
                ["version"] = 1,
                ["role"] = "TrustedOnline",
                ["accountSteamIds"] = new JArray(steamIds.Distinct().OrderBy(value => value, StringComparer.Ordinal)),
                ["capabilities"] = new JArray(capabilities.Distinct().OrderBy(value => value, StringComparer.Ordinal)),
                ["issuedAt"] = now,
                ["expiresAt"] = null,
                ["offlineWipeAfterHours"] = 168
            };
            string envelope = EncryptGrant(Encoding.UTF8.GetBytes(grant.ToString(Formatting.None)), inviteToken, settings.VaultId, inviteId);
            JObject body = new JObject
            {
                ["inviteId"] = inviteId,
                ["tokenHash"] = Base64Url(SHA256.HashData(Encoding.UTF8.GetBytes(inviteToken))),
                ["role"] = "TrustedOnline",
                ["expiresAt"] = expiresAt,
                ["deviceExpiresAt"] = null,
                ["grantEnvelope"] = envelope
            };
            await SendAsync(settings.RelayUrl, $"/v1/trust/{settings.VaultId}/invites", HttpMethod.Post, body, false, null, cancellationToken);
            string qr = $"sdapp://device-enroll?v=1&relay={Uri.EscapeDataString(settings.RelayUrl)}&vault={settings.VaultId}&invite={inviteId}&token={inviteToken}&exp={expiresAt}";
            return new DesktopDeviceInvitation { QrPayload = qr, ExpiresAt = expiresAt };
        }

        public Task RevokeAsync(DesktopRegistrySettings settings, string deviceId, CancellationToken token) =>
            SendAsync(settings.RelayUrl, $"/v1/trust/{settings.VaultId}/devices/{deviceId}", HttpMethod.Delete, null, false, null, token);

        public async Task<int> LockdownAsync(DesktopRegistrySettings settings, CancellationToken token)
        {
            JObject response = await SendAsync(settings.RelayUrl, $"/v1/trust/{settings.VaultId}/lockdown", HttpMethod.Post, null, false, null, token);
            return response.Value<int>("revoked");
        }

        private static async Task<JObject> SendAsync(string endpoint, string path, HttpMethod method, JObject json, bool includePublicKey, string bearer, CancellationToken token)
        {
            byte[] body = json == null ? Array.Empty<byte>() : Encoding.UTF8.GetBytes(json.ToString(Formatting.None));
            long timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            string nonce = RandomBase64Url(16);
            byte[] canonical = CanonicalRequest(method.Method, path, timestamp, nonce, body);
            DeviceIdentityInfo identity = DeviceIdentityStore.GetInfo();
            using HttpRequestMessage request = new HttpRequestMessage(method, endpoint.TrimEnd('/') + path);
            request.Headers.Add("x-sdapp-device-id", identity.DeviceId);
            request.Headers.Add("x-sdapp-timestamp", timestamp.ToString(System.Globalization.CultureInfo.InvariantCulture));
            request.Headers.Add("x-sdapp-nonce", nonce);
            request.Headers.Add("x-sdapp-signature", DeviceIdentityStore.Sign(canonical));
            if (includePublicKey) request.Headers.Add("x-sdapp-public-key", identity.PublicKeyDerBase64Url);
            if (!string.IsNullOrEmpty(bearer)) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
            if (method != HttpMethod.Get) request.Content = new ByteArrayContent(body) { Headers = { ContentType = new MediaTypeHeaderValue("application/json") } };
            using HttpResponseMessage response = await Http.SendAsync(request, token);
            string text = await response.Content.ReadAsStringAsync(token);
            if (!response.IsSuccessStatusCode) throw new InvalidOperationException(JObject.Parse(string.IsNullOrWhiteSpace(text) ? "{}" : text).Value<string>("error") ?? $"Registry HTTP {(int)response.StatusCode}");
            return string.IsNullOrWhiteSpace(text) ? new JObject() : JObject.Parse(text);
        }

        private static byte[] CanonicalRequest(string method, string path, long timestamp, string nonce, byte[] body)
        {
            string hash = Base64Url(SHA256.HashData(body));
            StringBuilder value = new StringBuilder("sdapp-device-request/v1\n");
            AddField(value, "method", method.ToUpperInvariant());
            AddField(value, "path", path);
            AddField(value, "timestamp", timestamp.ToString(System.Globalization.CultureInfo.InvariantCulture));
            AddField(value, "nonce", nonce);
            AddField(value, "bodyHash", hash);
            return Encoding.UTF8.GetBytes(value.ToString());
        }

        private static void AddField(StringBuilder builder, string name, string value) =>
            builder.Append(name).Append('=').Append(Encoding.UTF8.GetByteCount(value)).Append(':').Append(value).Append('\n');

        private static string EncryptGrant(byte[] plain, string token, string vaultId, string inviteId)
        {
            byte[] iv = RandomNumberGenerator.GetBytes(12);
            byte[] key = SHA256.HashData(Encoding.UTF8.GetBytes($"sdapp-invite-grant-key/v1\n{token}\n"));
            byte[] cipher = new byte[plain.Length];
            byte[] tag = new byte[16];
            try
            {
                using AesGcm aes = new AesGcm(key, 16);
                aes.Encrypt(iv, plain, cipher, tag, Encoding.UTF8.GetBytes($"sdapp-invite-grant/v1\n{vaultId}\n{inviteId}\n"));
                return Base64Url(iv.Concat(cipher).Concat(tag).ToArray());
            }
            finally
            {
                CryptographicOperations.ZeroMemory(key);
                CryptographicOperations.ZeroMemory(plain);
            }
        }

        private static string RandomBase64Url(int length) => Base64Url(RandomNumberGenerator.GetBytes(length));
        private static string Base64Url(byte[] value) => Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }
}
