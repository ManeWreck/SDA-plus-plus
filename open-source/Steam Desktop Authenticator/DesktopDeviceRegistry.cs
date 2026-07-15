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
        public string LastAuditEventId { get; set; } = string.Empty;
        public string AdminPinSalt { get; set; } = string.Empty;
        public string AdminPinHash { get; set; } = string.Empty;
        public int AdminPinIterations { get; set; }
        public int AdminPinFailedAttempts { get; set; }
        public long? AdminPinLockedUntil { get; set; }
        public string AdminDeviceId { get; set; } = string.Empty;
        public string AdminPublicKey { get; set; } = string.Empty;
        public string AdminEncryptionPublicKey { get; set; } = string.Empty;
        public string AdminEncryptionKeySignature { get; set; } = string.Empty;
        public string RemoteAuthorization { get; set; } = string.Empty;
        public string RemoteAuthorizationSignature { get; set; } = string.Empty;
    }

    internal sealed class DesktopRegistryDevice
    {
        [JsonProperty("deviceId")] public string DeviceId { get; set; }
        [JsonProperty("displayName")] public string DisplayName { get; set; }
        [JsonProperty("role")] public string Role { get; set; }
        [JsonProperty("lastSeenAt")] public long LastSeenAt { get; set; }
        [JsonProperty("expiresAt")] public long? ExpiresAt { get; set; }
        [JsonProperty("revokedAt")] public long? RevokedAt { get; set; }
        [JsonProperty("lockedAt")] public long? LockedAt { get; set; }
        [JsonProperty("publicKey")] public string PublicKey { get; set; }
        [JsonProperty("encryptionPublicKey")] public string EncryptionPublicKey { get; set; }
        [JsonProperty("encryptionKeySignature")] public string EncryptionKeySignature { get; set; }
        [JsonProperty("grantFingerprint")] public string GrantFingerprint { get; set; }
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
        [JsonProperty("createdAt")] public long CreatedAt { get; set; }
        [JsonProperty("expiresAt")] public long ExpiresAt { get; set; }
    }

    internal sealed class DesktopRemoteActionRequest
    {
        public string ActionId { get; init; }
        public string RequestedBy { get; init; }
        public string Kind { get; init; }
        public byte[] Payload { get; init; }
        public long CreatedAt { get; init; }
        public long ExpiresAt { get; init; }
        public string RequesterPublicKey { get; init; }
        public string RequesterEncryptionPublicKey { get; init; }
        public string RequesterEncryptionKeySignature { get; init; }
        public RemoteActionAuthorization Authorization { get; init; }
    }

    internal sealed class DesktopRemoteActionResult
    {
        public string ActionId { get; init; }
        public string Kind { get; init; }
        public string Status { get; init; }
        public byte[] ResponsePayload { get; init; }
        public long ExpiresAt { get; init; }
    }

    internal sealed class DesktopRegistryAuditEvent
    {
        [JsonProperty("eventId")] public string EventId { get; set; }
        [JsonProperty("action")] public string Action { get; set; }
        [JsonProperty("actorDeviceId")] public string ActorDeviceId { get; set; }
        [JsonProperty("targetDeviceId")] public string TargetDeviceId { get; set; }
        [JsonProperty("occurredAt")] public long OccurredAt { get; set; }
        [JsonProperty("country")] public string Country { get; set; }
    }

    internal sealed class DesktopSecurityWebhookStatus
    {
        [JsonProperty("configured")] public bool Configured { get; set; }
        [JsonProperty("endpointHost")] public string EndpointHost { get; set; }
        [JsonProperty("updatedAt")] public long? UpdatedAt { get; set; }
        [JsonProperty("lastDeliveryAt")] public long? LastDeliveryAt { get; set; }
        [JsonProperty("lastDeliveryOk")] public bool? LastDeliveryOk { get; set; }
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

        public static SteamGuardAccount[] BuildVisibleAccounts(
            IEnumerable<SteamGuardAccount> localAccounts,
            ISet<ulong> remoteSteamIds,
            Func<ulong, string> remoteAccountName)
        {
            return BuildVisibleAccountsForSettings(localAccounts, remoteSteamIds, remoteAccountName, Current);
        }

        internal static SteamGuardAccount[] BuildVisibleAccountsForSettings(
            IEnumerable<SteamGuardAccount> localAccounts,
            ISet<ulong> remoteSteamIds,
            Func<ulong, string> remoteAccountName,
            DesktopRegistrySettings settings)
        {
            if (remoteSteamIds == null) throw new ArgumentNullException(nameof(remoteSteamIds));
            if (remoteAccountName == null) throw new ArgumentNullException(nameof(remoteAccountName));
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            remoteSteamIds.Clear();
            SteamGuardAccount[] source = localAccounts?.ToArray() ?? Array.Empty<SteamGuardAccount>();
            bool trusted = !string.IsNullOrWhiteSpace(settings.Role) && settings.Role != "Admin";
            if (trusted && settings.ExpiresAt != null && settings.ExpiresAt <= DateTimeOffset.UtcNow.ToUnixTimeMilliseconds())
                return Array.Empty<SteamGuardAccount>();
            List<SteamGuardAccount> accounts = trusted
                ? source.Where(account => settings.GrantedAccountSteamIds.Contains(account.Session?.SteamID.ToString() ?? string.Empty)).ToList()
                : source.ToList();
            if (!trusted)
                return accounts.ToArray();

            HashSet<ulong> localSteamIds = accounts
                .Where(account => account?.Session != null)
                .Select(account => account.Session.SteamID)
                .ToHashSet();
            foreach (string grantedSteamId in settings.GrantedAccountSteamIds.OrderBy(value => value, StringComparer.Ordinal))
            {
                if (!ulong.TryParse(grantedSteamId, out ulong steamId) || localSteamIds.Contains(steamId)) continue;
                remoteSteamIds.Add(steamId);
                accounts.Add(new SteamGuardAccount
                {
                    AccountName = remoteAccountName(steamId),
                    Session = new SessionData { SteamID = steamId }
                });
            }
            return accounts.ToArray();
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
                ["keyProtection"] = identity.Protection,
                ["encryptionPublicKey"] = identity.EncryptionPublicKeyDerBase64Url,
                ["encryptionKeySignature"] = identity.EncryptionKeySignatureBase64Url
            };
            await SendAsync(endpoint, $"/v1/trust/{vaultId}/bootstrap", HttpMethod.Put, body, true, null, token);
            DesktopRegistrySettings previous = DesktopRegistrySettingsStore.Load();
            DesktopRegistrySettings settings = new DesktopRegistrySettings
            {
                VaultId = vaultId,
                RelayUrl = endpoint,
                Role = "Admin",
                AdminDeviceId = identity.DeviceId,
                AdminPublicKey = identity.PublicKeyDerBase64Url,
                AdminEncryptionPublicKey = identity.EncryptionPublicKeyDerBase64Url,
                AdminEncryptionKeySignature = identity.EncryptionKeySignatureBase64Url,
                AdminPinSalt = previous.AdminPinSalt,
                AdminPinHash = previous.AdminPinHash,
                AdminPinIterations = previous.AdminPinIterations,
                AdminPinFailedAttempts = previous.AdminPinFailedAttempts,
                AdminPinLockedUntil = previous.AdminPinLockedUntil
            };
            DesktopRegistrySettingsStore.Save(settings);
            return settings;
        }

        public async Task<IReadOnlyList<DesktopRegistryDevice>> ListDevicesAsync(DesktopRegistrySettings settings, CancellationToken token)
        {
            JObject response = await SendAsync(settings.RelayUrl, $"/v1/trust/{settings.VaultId}/devices", HttpMethod.Get, null, false, null, token);
            return response["devices"]?.ToObject<List<DesktopRegistryDevice>>() ?? new List<DesktopRegistryDevice>();
        }

        public async Task<IReadOnlyList<DesktopRegistryAuditEvent>> ListAuditAsync(DesktopRegistrySettings settings, CancellationToken token)
        {
            JObject response = await SendAsync(settings.RelayUrl, $"/v1/trust/{settings.VaultId}/audit", HttpMethod.Get, null, false, null, token);
            return response["events"]?.ToObject<List<DesktopRegistryAuditEvent>>() ?? new List<DesktopRegistryAuditEvent>();
        }

        public async Task<DesktopSecurityWebhookStatus> GetSecurityWebhookAsync(DesktopRegistrySettings settings, CancellationToken token)
        {
            JObject response = await SendAsync(settings.RelayUrl, $"/v1/trust/{settings.VaultId}/notifications", HttpMethod.Get, null, false, null, token);
            return response.ToObject<DesktopSecurityWebhookStatus>() ?? new DesktopSecurityWebhookStatus();
        }

        public async Task ConfigureSecurityWebhookAsync(
            DesktopRegistrySettings settings,
            string endpointUrl,
            string signingSecret,
            CancellationToken token)
        {
            await SendAsync(
                settings.RelayUrl,
                $"/v1/trust/{settings.VaultId}/notifications",
                HttpMethod.Put,
                new JObject
                {
                    ["endpointUrl"] = (endpointUrl ?? string.Empty).Trim(),
                    ["signingSecret"] = signingSecret
                },
                false,
                null,
                token);
        }

        public async Task TestSecurityWebhookAsync(DesktopRegistrySettings settings, CancellationToken token)
        {
            await SendAsync(settings.RelayUrl, $"/v1/trust/{settings.VaultId}/notifications/test", HttpMethod.Post, null, false, null, token);
        }

        public async Task RemoveSecurityWebhookAsync(DesktopRegistrySettings settings, CancellationToken token)
        {
            await SendAsync(settings.RelayUrl, $"/v1/trust/{settings.VaultId}/notifications", HttpMethod.Delete, null, false, null, token);
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
            string role,
            IEnumerable<SteamGuardAccount> selectedAccounts,
            IEnumerable<string> capabilities,
            CancellationToken cancellationToken)
        {
            if (role != "Admin" && role != "TrustedOnline" && role != "TrustedOffline" && role != "Temporary")
                throw new InvalidOperationException("Invalid device role.");
            SteamGuardAccount[] accountList = selectedAccounts?.Where(account => account?.Session != null)
                .GroupBy(account => account.Session.SteamID)
                .Select(group => group.First())
                .ToArray()
                ?? Array.Empty<SteamGuardAccount>();
            if (role != "Admin" && accountList.Length == 0) throw new InvalidOperationException("Select at least one account.");
            if (role == "Admin") accountList = Array.Empty<SteamGuardAccount>();
            string[] steamIds = accountList.Select(account => account.Session.SteamID.ToString()).Distinct().ToArray();
            JArray accountPayloads = new JArray();
            int payloadBytes = 0;
            if (role == "TrustedOffline")
            {
                foreach (SteamGuardAccount account in accountList)
                {
                    byte[] plain = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(account));
                    try
                    {
                        payloadBytes += plain.Length;
                        if (payloadBytes > 256 * 1024) throw new InvalidOperationException("Selected account data is too large for one invitation. Create multiple invitations.");
                        accountPayloads.Add(new JObject
                        {
                            ["steamId"] = account.Session.SteamID.ToString(),
                            ["maFile"] = Convert.ToBase64String(plain)
                        });
                    }
                    finally { CryptographicOperations.ZeroMemory(plain); }
                }
            }
            string inviteId = RandomBase64Url(16);
            string inviteToken = RandomBase64Url(32);
            string verificationCode = RandomVerificationCode();
            long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            long expiresAt = now + (long)TimeSpan.FromMinutes(5).TotalMilliseconds;
            long? deviceExpiresAt = role == "Temporary" ? now + (long)TimeSpan.FromHours(24).TotalMilliseconds : null;
            DeviceIdentityInfo adminIdentity = DeviceIdentityStore.GetInfo();
            RemoteActionAuthorization authorization = new RemoteActionAuthorization
            {
                VaultId = settings.VaultId,
                InviteId = inviteId,
                IssuerDeviceId = adminIdentity.DeviceId,
                Role = role,
                IssuedAt = now,
                ExpiresAt = deviceExpiresAt,
                AccountSteamIds = steamIds.ToHashSet(StringComparer.Ordinal),
                Capabilities = (role == "Admin" ? Array.Empty<string>() : capabilities ?? Array.Empty<string>())
                    .ToHashSet(StringComparer.Ordinal)
            };
            byte[] authorizationBytes = RemoteActionAuthorizationCodec.Canonical(authorization);
            string authorizationText = Base64Url(authorizationBytes);
            string authorizationSignature = DeviceIdentityStore.Sign(authorizationBytes);
            string grantFingerprint = RemoteActionAuthorizationCodec.Fingerprint(authorizationBytes);
            JObject grant = new JObject
            {
                ["version"] = 1,
                ["role"] = role,
                ["accountSteamIds"] = new JArray(steamIds.Distinct().OrderBy(value => value, StringComparer.Ordinal)),
                ["capabilities"] = new JArray((role == "Admin" ? Array.Empty<string>() : capabilities ?? Array.Empty<string>())
                    .Distinct().OrderBy(value => value, StringComparer.Ordinal)),
                ["issuedAt"] = now,
                ["expiresAt"] = deviceExpiresAt,
                ["offlineWipeAfterHours"] = role == "TrustedOffline" ? 168 : null,
                ["accountPayloads"] = accountPayloads,
                ["adminDeviceId"] = adminIdentity.DeviceId,
                ["adminPublicKey"] = adminIdentity.PublicKeyDerBase64Url,
                ["adminEncryptionPublicKey"] = adminIdentity.EncryptionPublicKeyDerBase64Url,
                ["adminEncryptionKeySignature"] = adminIdentity.EncryptionKeySignatureBase64Url,
                ["remoteAuthorization"] = authorizationText,
                ["remoteAuthorizationSignature"] = authorizationSignature
            };
            string envelope = EncryptGrant(Encoding.UTF8.GetBytes(grant.ToString(Formatting.None)), inviteToken, settings.VaultId, inviteId);
            JObject body = new JObject
            {
                ["inviteId"] = inviteId,
                ["tokenHash"] = Base64Url(SHA256.HashData(Encoding.UTF8.GetBytes(inviteToken))),
                ["verificationCodeHash"] = Base64Url(SHA256.HashData(Encoding.UTF8.GetBytes(verificationCode))),
                ["role"] = role,
                ["expiresAt"] = expiresAt,
                ["deviceExpiresAt"] = deviceExpiresAt,
                ["grantEnvelope"] = envelope,
                ["grantFingerprint"] = grantFingerprint
            };
            await SendAsync(settings.RelayUrl, $"/v1/trust/{settings.VaultId}/invites", HttpMethod.Post, body, false, null, cancellationToken);
            string qr = $"sdapp://device-enroll?v=1&relay={Uri.EscapeDataString(settings.RelayUrl)}&vault={settings.VaultId}&invite={inviteId}&token={inviteToken}&exp={expiresAt}";
            return new DesktopDeviceInvitation { QrPayload = qr, ExpiresAt = expiresAt, VerificationCode = verificationCode };
        }

        public async Task<DesktopRegistrySettings> ClaimInvitationAsync(string qrPayload, string verificationCode, string localVaultPassKey, CancellationToken token)
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
                ["verificationCode"] = verificationCode,
                ["encryptionPublicKey"] = identity.EncryptionPublicKeyDerBase64Url,
                ["encryptionKeySignature"] = identity.EncryptionKeySignatureBase64Url
            };
            JObject response = await SendAsync(endpoint, $"/v1/trust/{vaultId}/invites/{inviteId}/claim", HttpMethod.Put, body, true, inviteToken, token);
            byte[] grantBytes = DecryptGrant(response.Value<string>("grantEnvelope"), inviteToken, vaultId, inviteId);
            try
            {
                JObject grant = JObject.Parse(Encoding.UTF8.GetString(grantBytes));
                string adminDeviceId = grant.Value<string>("adminDeviceId") ?? string.Empty;
                string adminPublicKey = grant.Value<string>("adminPublicKey") ?? string.Empty;
                string adminEncryptionPublicKey = grant.Value<string>("adminEncryptionPublicKey") ?? string.Empty;
                string adminEncryptionKeySignature = grant.Value<string>("adminEncryptionKeySignature") ?? string.Empty;
                string authorizationText = grant.Value<string>("remoteAuthorization") ?? string.Empty;
                string authorizationSignature = grant.Value<string>("remoteAuthorizationSignature") ?? string.Empty;
                byte[] authorizationBytes = Base64UrlDecode(authorizationText);
                RemoteActionAuthorization authorization = RemoteActionAuthorizationCodec.Parse(authorizationBytes);
                HashSet<string> grantedIds = grant["accountSteamIds"]?.Values<string>().ToHashSet(StringComparer.Ordinal)
                    ?? new HashSet<string>(StringComparer.Ordinal);
                HashSet<string> grantedCapabilities = grant["capabilities"]?.Values<string>().ToHashSet(StringComparer.Ordinal)
                    ?? new HashSet<string>(StringComparer.Ordinal);
                DesktopRegistryDevice claimedDevice = response["device"]?.ToObject<DesktopRegistryDevice>()
                    ?? throw new InvalidOperationException("The claimed device response is invalid.");
                if (authorization.VaultId != vaultId || authorization.InviteId != inviteId ||
                    authorization.IssuerDeviceId != adminDeviceId || authorization.Role != grant.Value<string>("role") ||
                    authorization.Role != claimedDevice.Role || authorization.IssuedAt != grant.Value<long>("issuedAt") ||
                    !authorization.AccountSteamIds.SetEquals(grantedIds) ||
                    !authorization.Capabilities.SetEquals(grantedCapabilities) ||
                    authorization.ExpiresAt != grant.Value<long?>("expiresAt"))
                    throw new InvalidOperationException("The remote action authorization does not match the invitation.");
                if (!DeviceIdentityStore.Verify(adminPublicKey, authorizationBytes, authorizationSignature))
                    throw new InvalidOperationException("The remote action authorization signature is invalid.");
                if (!string.Equals(claimedDevice.GrantFingerprint, RemoteActionAuthorizationCodec.Fingerprint(authorizationBytes), StringComparison.Ordinal))
                    throw new InvalidOperationException("The remote action authorization is not bound to this device.");
                if (!DeviceIdentityStore.VerifyEncryptionKeyBinding(
                    adminDeviceId,
                    adminPublicKey,
                    adminEncryptionPublicKey,
                    adminEncryptionKeySignature))
                    throw new InvalidOperationException("The Admin encryption identity is invalid.");
                DesktopRegistrySettings settings = new DesktopRegistrySettings
                {
                    VaultId = vaultId,
                    RelayUrl = endpoint,
                    Role = grant.Value<string>("role"),
                    GrantedAccountSteamIds = grantedIds,
                    Capabilities = grantedCapabilities,
                    ExpiresAt = grant.Value<long?>("expiresAt"),
                    OfflineWipeAfterHours = grant.Value<int?>("offlineWipeAfterHours"),
                    LastSuccessfulHeartbeatAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                    AdminDeviceId = adminDeviceId,
                    AdminPublicKey = adminPublicKey,
                    AdminEncryptionPublicKey = adminEncryptionPublicKey,
                    AdminEncryptionKeySignature = adminEncryptionKeySignature,
                    RemoteAuthorization = authorizationText,
                    RemoteAuthorizationSignature = authorizationSignature
                };
                DesktopRegistrySettingsStore.Save(settings);
                ImportGrantedAccounts(grant, settings, localVaultPassKey);
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

        public async Task<string> RequestRemoteActionAsync(
            DesktopRegistrySettings settings,
            string kind,
            byte[] payload,
            CancellationToken token)
        {
            if (settings.Role != "TrustedOnline" && settings.Role != "Temporary")
                throw new InvalidOperationException("Only Trusted Online or Temporary devices can request remote actions.");
            if (!RemoteActionKinds.Contains(kind) || !settings.Capabilities.Contains(kind))
                throw new InvalidOperationException("Remote action is not granted.");
            if (string.IsNullOrWhiteSpace(settings.AdminEncryptionPublicKey))
                throw new InvalidOperationException("Admin encryption identity is missing.");
            if (string.IsNullOrWhiteSpace(settings.RemoteAuthorization) ||
                string.IsNullOrWhiteSpace(settings.RemoteAuthorizationSignature))
                throw new InvalidOperationException("Pair this device again to enable secure remote actions.");
            byte[] authorizationBytes = Base64UrlDecode(settings.RemoteAuthorization);
            RemoteActionAuthorization authorization = RemoteActionAuthorizationCodec.Parse(authorizationBytes);
            if (authorization.VaultId != settings.VaultId || authorization.Role != settings.Role ||
                !authorization.AccountSteamIds.SetEquals(settings.GrantedAccountSteamIds) ||
                !authorization.Capabilities.SetEquals(settings.Capabilities) ||
                authorization.IssuerDeviceId != settings.AdminDeviceId ||
                (authorization.ExpiresAt != null && authorization.ExpiresAt <= DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()) ||
                !authorization.Capabilities.Contains(kind))
                throw new InvalidOperationException("Remote action authorization is invalid or expired.");
            if (!DeviceIdentityStore.Verify(settings.AdminPublicKey, authorizationBytes, settings.RemoteAuthorizationSignature))
                throw new CryptographicException("Remote action authorization signature is invalid.");
            string actionId = Guid.NewGuid().ToString("D");
            byte[] authorizedPayload = Encoding.UTF8.GetBytes(new JObject
            {
                ["authorization"] = settings.RemoteAuthorization,
                ["authorizationSignature"] = settings.RemoteAuthorizationSignature,
                ["issuerPublicKey"] = settings.AdminPublicKey,
                ["payload"] = Base64Url(payload)
            }.ToString(Formatting.None));
            string envelope = DeviceIdentityStore.EncryptRemoteAction(
                settings.AdminEncryptionPublicKey,
                authorizedPayload,
                settings.VaultId,
                actionId,
                "request");
            await SendAsync(
                settings.RelayUrl,
                $"/v1/trust/{settings.VaultId}/actions",
                HttpMethod.Post,
                new JObject
                {
                    ["actionId"] = actionId,
                    ["kind"] = kind,
                    ["requestEnvelope"] = envelope,
                    ["expiresAt"] = DateTimeOffset.UtcNow.AddMinutes(2).ToUnixTimeMilliseconds()
                },
                false,
                null,
                token);
            return actionId;
        }

        public Task<string> RequestGuardCodeAsync(
            DesktopRegistrySettings settings,
            string steamId,
            CancellationToken token) =>
            RequestRemoteActionAsync(settings, "ViewGuardCode", GuardCodeRemoteActionCodec.Request(steamId), token);

        public Task<string> RequestSteamQrApprovalAsync(
            DesktopRegistrySettings settings,
            string steamId,
            string qrPayload,
            CancellationToken token) =>
            RequestRemoteActionAsync(
                settings,
                "ApproveSteamQr",
                SteamQrRemoteActionCodec.Request(steamId, qrPayload),
                token);

        public async Task<IReadOnlyList<DesktopRemoteActionRequest>> PendingRemoteActionsAsync(
            DesktopRegistrySettings settings,
            CancellationToken token)
        {
            if (settings.Role != "Admin") throw new InvalidOperationException("Admin role required.");
            Dictionary<string, DesktopRegistryDevice> activeAdmins = (await ListDevicesAsync(settings, token))
                .Where(device => device.Role == "Admin" && device.RevokedAt == null &&
                    (device.ExpiresAt == null || device.ExpiresAt > DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()))
                .ToDictionary(device => device.DeviceId, StringComparer.Ordinal);
            JObject response = await SendAsync(
                settings.RelayUrl,
                $"/v1/trust/{settings.VaultId}/actions",
                HttpMethod.Get,
                null,
                false,
                null,
                token);
            List<DesktopRemoteActionRequest> result = new List<DesktopRemoteActionRequest>();
            foreach (JObject action in response["actions"]?.OfType<JObject>() ?? Enumerable.Empty<JObject>())
            {
                try
                {
                    string requestedBy = action.Value<string>("requestedBy") ?? string.Empty;
                    string requesterPublicKey = action.Value<string>("requesterPublicKey") ?? string.Empty;
                    string requesterEncryptionPublicKey = action.Value<string>("requesterEncryptionPublicKey") ?? string.Empty;
                    string requesterEncryptionKeySignature = action.Value<string>("requesterEncryptionKeySignature") ?? string.Empty;
                    string requesterRole = action.Value<string>("requesterRole") ?? string.Empty;
                    string requesterGrantFingerprint = action.Value<string>("requesterGrantFingerprint") ?? string.Empty;
                    if (!DeviceIdentityStore.VerifyEncryptionKeyBinding(
                        requestedBy,
                        requesterPublicKey,
                        requesterEncryptionPublicKey,
                        requesterEncryptionKeySignature))
                        throw new CryptographicException("Remote action device identity is invalid.");
                    string actionId = action.Value<string>("actionId") ?? string.Empty;
                    byte[] decrypted = DeviceIdentityStore.DecryptRemoteAction(
                        action.Value<string>("requestEnvelope"),
                        settings.VaultId,
                        actionId,
                        "request");
                    JObject authorizedPayload;
                    try { authorizedPayload = JObject.Parse(Encoding.UTF8.GetString(decrypted)); }
                    finally { CryptographicOperations.ZeroMemory(decrypted); }
                    byte[] authorizationBytes = Base64UrlDecode(authorizedPayload.Value<string>("authorization") ?? string.Empty);
                    RemoteActionAuthorization authorization = RemoteActionAuthorizationCodec.Parse(authorizationBytes);
                    string issuerPublicKey = authorizedPayload.Value<string>("issuerPublicKey") ?? string.Empty;
                    activeAdmins.TryGetValue(authorization.IssuerDeviceId, out DesktopRegistryDevice issuer);
                    if (RemoteActionAuthorizationCodec.Fingerprint(authorizationBytes) != requesterGrantFingerprint ||
                        authorization.VaultId != settings.VaultId || authorization.Role != requesterRole ||
                        !authorization.Capabilities.Contains(action.Value<string>("kind") ?? string.Empty) ||
                        (authorization.ExpiresAt != null && authorization.ExpiresAt <= DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()) ||
                        issuer?.PublicKey != issuerPublicKey)
                        throw new CryptographicException("Remote action grant is invalid or expired.");
                    if (!DeviceIdentityStore.Verify(
                        issuerPublicKey,
                        authorizationBytes,
                        authorizedPayload.Value<string>("authorizationSignature") ?? string.Empty))
                        throw new CryptographicException("Remote action authorization signature is invalid.");
                    result.Add(new DesktopRemoteActionRequest
                    {
                        ActionId = actionId,
                        RequestedBy = requestedBy,
                        Kind = action.Value<string>("kind") ?? string.Empty,
                        Payload = Base64UrlDecode(authorizedPayload.Value<string>("payload") ?? string.Empty),
                        CreatedAt = action.Value<long>("createdAt"),
                        ExpiresAt = action.Value<long>("expiresAt"),
                        RequesterPublicKey = requesterPublicKey,
                        RequesterEncryptionPublicKey = requesterEncryptionPublicKey,
                        RequesterEncryptionKeySignature = requesterEncryptionKeySignature,
                        Authorization = authorization
                    });
                }
                catch (Exception ex) when (
                    ex is JsonException ||
                    ex is FormatException ||
                    ex is ArgumentException ||
                    ex is CryptographicException ||
                    ex is InvalidOperationException)
                {
                    // One malformed or forged request must not block other devices' valid actions.
                }
            }
            return result;
        }

        public async Task RespondRemoteActionAsync(
            DesktopRegistrySettings settings,
            DesktopRemoteActionRequest action,
            byte[] payload,
            CancellationToken token)
        {
            if (settings.Role != "Admin") throw new InvalidOperationException("Admin role required.");
            if (action == null || action.ExpiresAt <= DateTimeOffset.UtcNow.ToUnixTimeMilliseconds())
                throw new InvalidOperationException("Remote action has expired.");
            if (!DeviceIdentityStore.VerifyEncryptionKeyBinding(
                action.RequestedBy,
                action.RequesterPublicKey,
                action.RequesterEncryptionPublicKey,
                action.RequesterEncryptionKeySignature))
                throw new CryptographicException("Remote action device identity is invalid.");
            string envelope = DeviceIdentityStore.EncryptRemoteAction(
                action.RequesterEncryptionPublicKey,
                payload,
                settings.VaultId,
                action.ActionId,
                "response");
            await SendAsync(
                settings.RelayUrl,
                $"/v1/trust/{settings.VaultId}/actions/{action.ActionId}/response",
                HttpMethod.Put,
                new JObject { ["responseEnvelope"] = envelope },
                false,
                null,
                token);
        }

        public async Task<DesktopRemoteActionResult> GetRemoteActionResultAsync(
            DesktopRegistrySettings settings,
            string actionId,
            CancellationToken token)
        {
            JObject response = (await SendAsync(
                settings.RelayUrl,
                $"/v1/trust/{settings.VaultId}/actions/{actionId}",
                HttpMethod.Get,
                null,
                false,
                null,
                token))["action"] as JObject ?? throw new InvalidOperationException("Invalid remote action response.");
            string envelope = response.Value<string>("responseEnvelope");
            return new DesktopRemoteActionResult
            {
                ActionId = actionId,
                Kind = response.Value<string>("kind") ?? string.Empty,
                Status = response.Value<string>("status") ?? string.Empty,
                ResponsePayload = string.IsNullOrWhiteSpace(envelope) ? null : DeviceIdentityStore.DecryptRemoteAction(
                    envelope,
                    settings.VaultId,
                    actionId,
                    "response"),
                ExpiresAt = response.Value<long>("expiresAt")
            };
        }

        public async Task<GuardCodeRemoteResponse> ConsumeGuardCodeResultAsync(
            DesktopRegistrySettings settings,
            string actionId,
            CancellationToken token)
        {
            DesktopRemoteActionResult result = await GetRemoteActionResultAsync(settings, actionId, token);
            if (result.Kind != "ViewGuardCode") throw new InvalidOperationException("Unexpected remote action response.");
            if (result.ResponsePayload == null) return null;
            GuardCodeRemoteResponse response = GuardCodeRemoteActionCodec.ParseResponse(result.ResponsePayload);
            if (response.SteamId != null && !settings.GrantedAccountSteamIds.Contains(response.SteamId))
                throw new CryptographicException("Remote Guard code account is not granted.");
            await AcknowledgeRemoteActionAsync(settings, actionId, token);
            return response;
        }

        public async Task<SteamQrRemoteResponse> ConsumeSteamQrApprovalResultAsync(
            DesktopRegistrySettings settings,
            string actionId,
            CancellationToken token)
        {
            DesktopRemoteActionResult result = await GetRemoteActionResultAsync(settings, actionId, token);
            if (result.Kind != "ApproveSteamQr") throw new InvalidOperationException("Unexpected remote action response.");
            if (result.ResponsePayload == null) return null;
            SteamQrRemoteResponse response = SteamQrRemoteActionCodec.ParseResponse(result.ResponsePayload);
            if (response.SteamId != null && !settings.GrantedAccountSteamIds.Contains(response.SteamId))
                throw new CryptographicException("Remote QR approval account is not granted.");
            await AcknowledgeRemoteActionAsync(settings, actionId, token);
            return response;
        }

        public Task AcknowledgeRemoteActionAsync(DesktopRegistrySettings settings, string actionId, CancellationToken token) =>
            SendAsync(
                settings.RelayUrl,
                $"/v1/trust/{settings.VaultId}/actions/{actionId}/ack",
                HttpMethod.Post,
                null,
                false,
                null,
                token);

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
            if (!response.IsSuccessStatusCode)
            {
                string registryError = $"Registry HTTP {(int)response.StatusCode}";
                if (!string.IsNullOrWhiteSpace(text))
                {
                    try { registryError = JObject.Parse(text).Value<string>("error") ?? registryError; }
                    catch (JsonException) { }
                }
                throw new DesktopRegistryException(response.StatusCode, registryError);
            }
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

        private static void ImportGrantedAccounts(JObject grant, DesktopRegistrySettings settings, string passKey)
        {
            JArray payloads = grant["accountPayloads"] as JArray;
            if (payloads == null || payloads.Count == 0) return;
            if (settings.Role != "TrustedOffline") throw new InvalidOperationException("This device role cannot receive account secrets.");
            if (payloads.Count > 64) throw new InvalidOperationException("The account grant contains too many payloads.");
            Manifest manifest = Manifest.GetManifest(true);
            if (manifest.Encrypted && string.IsNullOrEmpty(passKey)) throw new InvalidOperationException("The local vault passkey is required to import granted accounts.");
            int totalBytes = 0;
            HashSet<string> importedIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (JObject payload in payloads.OfType<JObject>())
            {
                string steamId = payload.Value<string>("steamId") ?? string.Empty;
                if (!settings.GrantedAccountSteamIds.Contains(steamId) || !importedIds.Add(steamId))
                    throw new InvalidOperationException("The account grant contains an invalid payload.");
                byte[] plain = Convert.FromBase64String(payload.Value<string>("maFile") ?? string.Empty);
                try
                {
                    totalBytes += plain.Length;
                    if (totalBytes > 256 * 1024) throw new InvalidOperationException("The account grant is too large.");
                    SteamGuardAccount account = JsonConvert.DeserializeObject<SteamGuardAccount>(Encoding.UTF8.GetString(plain));
                    if (account?.Session == null || account.Session.SteamID.ToString() != steamId)
                        throw new InvalidOperationException("The account payload does not match its grant.");
                    if (!manifest.SaveAccount(account, manifest.Encrypted, passKey))
                        throw new InvalidOperationException("SDA++ could not save a granted account into the local vault.");
                }
                finally { CryptographicOperations.ZeroMemory(plain); }
            }
            if (!importedIds.SetEquals(settings.GrantedAccountSteamIds))
                throw new InvalidOperationException("The account payload does not match the granted accounts.");
        }

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

        private static readonly HashSet<string> RemoteActionKinds = new HashSet<string>(StringComparer.Ordinal)
        {
            "ViewGuardCode",
            "ApproveSteamQr",
            "ViewConfirmations",
            "ApproveConfirmations",
            "TerminateSessions"
        };
    }
}
