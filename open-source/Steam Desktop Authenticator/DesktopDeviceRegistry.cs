using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SteamAuth;
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
        public HashSet<string> GrantedAccountSteamIds { get; set; } = new HashSet<string>();
        public HashSet<string> Capabilities { get; set; } = new HashSet<string>();
        public long? ExpiresAt { get; set; }
        public int? OfflineWipeAfterHours { get; set; }
        public long? LastSuccessfulHeartbeatAt { get; set; }
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
        public string VerificationCode { get; init; }
    }

    internal sealed class DesktopRegistryCommand
    {
        [JsonProperty("commandId")] public string CommandId { get; set; }
        [JsonProperty("type")] public string Type { get; set; }
        [JsonProperty("expiresAt")] public long ExpiresAt { get; set; }
    }

    internal static class DesktopRegistrySettingsStore
    {
        private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("SDA++ desktop device registry v1");
        private static readonly object Sync = new object();
        private static DesktopRegistrySettings cachedSettings;
        private static string FilePath => Path.Combine(Manifest.GetExecutableDir(), "maFiles", "device-registry.secure");

        public static DesktopRegistrySettings Load()
        {
            lock (Sync)
            {
                if (cachedSettings != null) return cachedSettings;
                if (!File.Exists(FilePath)) return cachedSettings = new DesktopRegistrySettings();
                byte[] encrypted = File.ReadAllBytes(FilePath);
                byte[] plain = ProtectedData.Unprotect(encrypted, Entropy, DataProtectionScope.CurrentUser);
                try
                {
                    return cachedSettings = JsonConvert.DeserializeObject<DesktopRegistrySettings>(Encoding.UTF8.GetString(plain))
                        ?? new DesktopRegistrySettings();
                }
                finally { CryptographicOperations.ZeroMemory(plain); }
            }
        }

        public static void Save(DesktopRegistrySettings settings)
        {
            lock (Sync)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
                byte[] plain = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(settings));
                try
                {
                    File.WriteAllBytes(FilePath, ProtectedData.Protect(plain, Entropy, DataProtectionScope.CurrentUser));
                    cachedSettings = settings;
                }
                finally { CryptographicOperations.ZeroMemory(plain); }
            }
        }

        public static void Clear()
        {
            lock (Sync)
            {
                if (File.Exists(FilePath)) File.Delete(FilePath);
                cachedSettings = null;
            }
        }
    }

    internal static class DesktopDeviceAccess
    {
        public static DesktopRegistrySettings Current => DesktopRegistrySettingsStore.Load();

        public static SteamGuardAccount[] FilterAccounts(IEnumerable<SteamGuardAccount> accounts)
        {
            SteamGuardAccount[] source = accounts?.ToArray() ?? Array.Empty<SteamGuardAccount>();
            DesktopRegistrySettings settings = Current;
            if (string.IsNullOrWhiteSpace(settings.Role) || settings.Role == "Admin") return source;
            if (settings.ExpiresAt != null && settings.ExpiresAt <= DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()) return Array.Empty<SteamGuardAccount>();
            return source.Where(account => settings.GrantedAccountSteamIds.Contains(account.Session?.SteamID.ToString() ?? string.Empty)).ToArray();
        }

        public static bool Permits(string capability, SteamGuardAccount account = null)
        {
            DesktopRegistrySettings settings = Current;
            if (string.IsNullOrWhiteSpace(settings.Role) || settings.Role == "Admin") return true;
            if (settings.ExpiresAt != null && settings.ExpiresAt <= DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()) return false;
            string steamId = account?.Session?.SteamID.ToString();
            return settings.Capabilities.Contains(capability) &&
                (account == null || settings.GrantedAccountSteamIds.Contains(steamId ?? string.Empty));
        }

        public static bool IsTrusted => !string.IsNullOrWhiteSpace(Current.Role) && Current.Role != "Admin";
        public static bool CanManageLocalVault => !IsTrusted;
    }

    internal sealed class DesktopDeviceRegistryClient
    {
        private static readonly HttpClient Http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };

        public async Task<DesktopRegistrySettings> BootstrapAdminAsync(string endpoint, string displayName, CancellationToken token)
        {
            endpoint = PairingRelayProfile.NormalizeEndpoint(endpoint);
            await PairingRelayProfile.VerifyRegistryAsync(endpoint, token);
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

        public async Task HeartbeatAsync(DesktopRegistrySettings settings, CancellationToken token)
        {
            await SendAsync(settings.RelayUrl, $"/v1/trust/{settings.VaultId}/heartbeat", HttpMethod.Post, null, false, null, token);
        }

        public async Task<IReadOnlyList<DesktopRegistryCommand>> PendingCommandsAsync(DesktopRegistrySettings settings, CancellationToken token)
        {
            JObject response = await SendAsync(settings.RelayUrl, $"/v1/trust/{settings.VaultId}/commands", HttpMethod.Get, null, false, null, token);
            return response["commands"]?.ToObject<List<DesktopRegistryCommand>>() ?? new List<DesktopRegistryCommand>();
        }

        public async Task AcknowledgeCommandAsync(DesktopRegistrySettings settings, string commandId, bool succeeded, CancellationToken token)
        {
            await SendAsync(
                settings.RelayUrl,
                $"/v1/trust/{settings.VaultId}/commands/{commandId}/ack",
                HttpMethod.Post,
                new JObject { ["status"] = succeeded ? "Completed" : "Failed" },
                false,
                null,
                token);
        }

        public async Task<DesktopDeviceInvitation> CreateInvitationAsync(
            DesktopRegistrySettings settings,
            IEnumerable<string> steamIds,
            IEnumerable<string> capabilities,
            CancellationToken cancellationToken)
        {
            string inviteId = RandomBase64Url(16);
            string inviteToken = RandomBase64Url(32);
            string verificationCode = RandomVerificationCode();
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
                ["verificationCodeHash"] = Base64Url(SHA256.HashData(Encoding.UTF8.GetBytes(verificationCode))),
                ["role"] = "TrustedOnline",
                ["expiresAt"] = expiresAt,
                ["deviceExpiresAt"] = null,
                ["grantEnvelope"] = envelope
            };
            await SendAsync(settings.RelayUrl, $"/v1/trust/{settings.VaultId}/invites", HttpMethod.Post, body, false, null, cancellationToken);
            string qr = $"sdapp://device-enroll?v=1&relay={Uri.EscapeDataString(settings.RelayUrl)}&vault={settings.VaultId}&invite={inviteId}&token={inviteToken}&exp={expiresAt}";
            return new DesktopDeviceInvitation { QrPayload = qr, ExpiresAt = expiresAt, VerificationCode = verificationCode };
        }

        public async Task<DesktopRegistrySettings> ClaimInvitationAsync(string qrPayload, string verificationCode, CancellationToken token)
        {
            Uri uri = new Uri(qrPayload ?? string.Empty);
            if (uri.Scheme != "sdapp" || uri.Host != "device-enroll") throw new InvalidOperationException("Invalid SDA++ device invitation.");
            Dictionary<string, string> query = ParseQuery(uri.Query);
            string endpoint = PairingRelayProfile.NormalizeEndpoint(query.GetValueOrDefault("relay"));
            string vaultId = query.GetValueOrDefault("vault") ?? string.Empty;
            string inviteId = query.GetValueOrDefault("invite") ?? string.Empty;
            string inviteToken = query.GetValueOrDefault("token") ?? string.Empty;
            await PairingRelayProfile.VerifyRegistryAsync(endpoint, token);
            verificationCode = new string((verificationCode ?? string.Empty).Where(char.IsDigit).ToArray());
            if (verificationCode.Length != 6)
                throw new InvalidOperationException(Localizer.Choose("Enter the 6-digit device verification code.", "Введите шестизначный код подтверждения устройства."));

            DeviceIdentityInfo identity = DeviceIdentityStore.GetInfo();
            JObject body = new JObject
            {
                ["displayName"] = Environment.MachineName.Substring(0, Math.Min(Environment.MachineName.Length, 64)),
                ["keyProtection"] = identity.Protection,
                ["verificationCode"] = verificationCode
            };
            JObject response = await SendAsync(endpoint, $"/v1/trust/{vaultId}/invites/{inviteId}/claim", HttpMethod.Put, body, true, inviteToken, token);
            byte[] grantBytes = DecryptGrant(response.Value<string>("grantEnvelope"), inviteToken, vaultId, inviteId);
            try
            {
                JObject grant = JObject.Parse(Encoding.UTF8.GetString(grantBytes));
                DesktopRegistrySettings settings = new DesktopRegistrySettings
                {
                    VaultId = vaultId,
                    RelayUrl = endpoint,
                    Role = grant.Value<string>("role"),
                    GrantedAccountSteamIds = grant["accountSteamIds"]?.Values<string>().ToHashSet(StringComparer.Ordinal) ?? new HashSet<string>(),
                    Capabilities = grant["capabilities"]?.Values<string>().ToHashSet(StringComparer.Ordinal) ?? new HashSet<string>(),
                    ExpiresAt = grant.Value<long?>("expiresAt"),
                    OfflineWipeAfterHours = grant.Value<int?>("offlineWipeAfterHours"),
                    LastSuccessfulHeartbeatAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                };
                DesktopRegistrySettingsStore.Save(settings);
                return settings;
            }
            finally { CryptographicOperations.ZeroMemory(grantBytes); }
        }

        public Task RevokeAsync(DesktopRegistrySettings settings, string deviceId, CancellationToken token) =>
            SendAsync(settings.RelayUrl, $"/v1/trust/{settings.VaultId}/devices/{deviceId}", HttpMethod.Delete, null, false, null, token);

        public async Task IssueCommandAsync(DesktopRegistrySettings settings, string deviceId, string type, CancellationToken token)
        {
            JObject body = new JObject
            {
                ["commandId"] = Guid.NewGuid().ToString(),
                ["type"] = type,
                ["expiresAt"] = DateTimeOffset.UtcNow.AddHours(24).ToUnixTimeMilliseconds()
            };
            await SendAsync(settings.RelayUrl, $"/v1/trust/{settings.VaultId}/devices/{deviceId}/commands", HttpMethod.Post, body, false, null, token);
        }

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

        private static byte[] DecryptGrant(string envelope, string token, string vaultId, string inviteId)
        {
            byte[] payload = Base64UrlDecode(envelope);
            if (payload.Length <= 28) throw new InvalidOperationException("Invalid encrypted account grant.");
            byte[] key = SHA256.HashData(Encoding.UTF8.GetBytes($"sdapp-invite-grant-key/v1\n{token}\n"));
            byte[] plain = new byte[payload.Length - 28];
            try
            {
                using AesGcm aes = new AesGcm(key, 16);
                aes.Decrypt(
                    payload.AsSpan(0, 12),
                    payload.AsSpan(12, plain.Length),
                    payload.AsSpan(12 + plain.Length, 16),
                    plain,
                    Encoding.UTF8.GetBytes($"sdapp-invite-grant/v1\n{vaultId}\n{inviteId}\n"));
                return plain;
            }
            finally
            {
                CryptographicOperations.ZeroMemory(key);
                CryptographicOperations.ZeroMemory(payload);
            }
        }

        private static string RandomVerificationCode() =>
            RandomNumberGenerator.GetInt32(1_000_000).ToString("D6", System.Globalization.CultureInfo.InvariantCulture);

        private static Dictionary<string, string> ParseQuery(string query) =>
            (query ?? string.Empty).TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries)
                .Select(part => part.Split('=', 2))
                .ToDictionary(part => Uri.UnescapeDataString(part[0]), part => Uri.UnescapeDataString(part.Length > 1 ? part[1] : string.Empty));

        private static string RandomBase64Url(int length) => Base64Url(RandomNumberGenerator.GetBytes(length));
        private static string Base64Url(byte[] value) => Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        private static byte[] Base64UrlDecode(string value)
        {
            string padded = (value ?? string.Empty).Replace('-', '+').Replace('_', '/');
            padded += new string('=', (4 - padded.Length % 4) % 4);
            return Convert.FromBase64String(padded);
        }
    }
}
