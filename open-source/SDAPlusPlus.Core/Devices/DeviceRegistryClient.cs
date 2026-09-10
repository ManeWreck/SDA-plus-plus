using System.Globalization;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace SDAPlusPlus.Core.Devices;

public sealed class DeviceRegistryClient(HttpClient httpClient, IDeviceRequestSigner signer) : IDeviceRegistryClient
{
    private static readonly System.Text.RegularExpressions.Regex IdPattern = new("^[A-Za-z0-9_-]{22}$", System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    public async Task<PendingDeviceCommandsResult> PendingCommandsAsync(DeviceRegistrySettings settings, CancellationToken cancellationToken = default)
    {
        if (!settings.IsConfigured || settings.IsExpired)
            return new(false, [], "An active device identity is required.");
        var (success, json, detail) = await SendAsync(settings, $"/v1/trust/{settings.VaultId}/commands", HttpMethod.Get, null, cancellationToken);
        if (!success) return new(false, [], detail);
        if (json?["commands"] is not JArray entries || entries.Count > 100)
            return new(false, [], "Invalid command list.");
        List<IncomingDeviceCommand> commands = [];
        HashSet<string> identifiers = new(StringComparer.Ordinal);
        foreach (var entry in entries)
        {
            if (entry is not JObject item || item["commandId"]?.Type != JTokenType.String ||
                item["type"]?.Type != JTokenType.String || item["expiresAt"]?.Type != JTokenType.Integer)
                return new(false, [], "Invalid command data.");
            string id = item.Value<string>("commandId")!;
            string type = item.Value<string>("type")!;
            if (!Guid.TryParseExact(id, "D", out _) || !identifiers.Add(id) ||
                type is not ("Lock" or "WipeLocalData" or "TerminateSteamSessions") ||
                !long.TryParse(item["expiresAt"]!.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out long expiresAt) || expiresAt <= 0 ||
                (item["vaultId"] is not null && item.Value<string>("vaultId") != settings.VaultId) ||
                (item["deviceId"] is not null && item.Value<string>("deviceId") != signer.DeviceId))
                return new(false, [], "Invalid command identity, type or expiry.");
            // Legacy responses omit target fields; bind them to the signed request identity.
            commands.Add(new(settings.VaultId, signer.DeviceId, id, type, expiresAt));
        }
        return new(true, commands);
    }

    public async Task<DeviceCommandResult> AcknowledgeCommandAsync(DeviceRegistrySettings settings, DeviceExecutionReceipt receipt, CancellationToken cancellationToken = default)
    {
        if (!settings.IsConfigured || settings.IsExpired || receipt.Command.VaultId != settings.VaultId ||
            receipt.Command.DeviceId != signer.DeviceId || !Guid.TryParseExact(receipt.Command.CommandId, "D", out _) ||
            receipt.State is not (DeviceExecutionState.Completed or DeviceExecutionState.Failed))
            return new(false, "Only a final result for this device can be acknowledged by the legacy protocol.");
        var (success, _, detail) = await SendAsync(settings,
            $"/v1/trust/{settings.VaultId}/commands/{receipt.Command.CommandId}/ack", HttpMethod.Post,
            new JObject { ["status"] = receipt.State.ToString() }, cancellationToken);
        return new(success, detail);
    }
    public async Task<DeviceListResult> ListDevicesAsync(
        DeviceRegistrySettings settings,
        CancellationToken cancellationToken = default)
    {
        if (!settings.IsConfigured || string.IsNullOrWhiteSpace(settings.RelayUrl))
            return new DeviceListResult(false, [], "The device registry is not configured.");

        string path = $"/v1/trust/{settings.VaultId}/devices";
        (bool success, JObject? json, string? detail) = await SendAsync(settings, path, HttpMethod.Get, null, cancellationToken);
        if (!success)
            return new DeviceListResult(false, [], detail);

        IReadOnlyList<RegistryDevice> devices = json?["devices"]?.ToObject<List<RegistryDevice>>() ?? [];
        return new DeviceListResult(true, devices);
    }

    public async Task<DeviceAuditResult> ListAuditAsync(
        DeviceRegistrySettings settings,
        CancellationToken cancellationToken = default)
    {
        string path = $"/v1/trust/{settings.VaultId}/audit";
        (bool success, JObject? json, string? detail) = await SendAsync(settings, path, HttpMethod.Get, null, cancellationToken);
        if (!success)
            return new DeviceAuditResult(false, [], detail);

        IReadOnlyList<RegistryAuditEvent> events = json?["events"]?.ToObject<List<RegistryAuditEvent>>() ?? [];
        return new DeviceAuditResult(true, events);
    }

    public async Task<DeviceCommandResult> RevokeAsync(
        DeviceRegistrySettings settings,
        string deviceId,
        CancellationToken cancellationToken = default)
    {
        if (!settings.IsConfigured || settings.Role != "Admin" || settings.IsExpired || deviceId == signer.DeviceId || string.IsNullOrWhiteSpace(deviceId))
            return new DeviceCommandResult(false, "A target device is required.");
        (bool success, _, string? detail) = await SendAsync(
            settings,
            $"/v1/trust/{settings.VaultId}/devices/{Uri.EscapeDataString(deviceId)}",
            HttpMethod.Delete,
            null,
            cancellationToken);
        return new DeviceCommandResult(success, detail);
    }

    public async Task<DeviceCommandResult> IssueCommandAsync(
        DeviceRegistrySettings settings,
        string deviceId,
        string commandType,
        CancellationToken cancellationToken = default)
    {
        if (!settings.IsConfigured || settings.Role != "Admin" || settings.IsExpired || deviceId == signer.DeviceId || string.IsNullOrWhiteSpace(deviceId) || commandType is not ("Lock" or "WipeLocalData" or "TerminateSteamSessions"))
            return new DeviceCommandResult(false, "The device command is invalid.");

        JObject body = new()
        {
            ["commandId"] = Guid.NewGuid().ToString(),
            ["type"] = commandType,
            ["expiresAt"] = DateTimeOffset.UtcNow.AddHours(24).ToUnixTimeMilliseconds()
        };
        (bool success, _, string? detail) = await SendAsync(
            settings,
            $"/v1/trust/{settings.VaultId}/devices/{Uri.EscapeDataString(deviceId)}/commands",
            HttpMethod.Post,
            body,
            cancellationToken);
        return new DeviceCommandResult(success, detail);
    }

    public async Task<DeviceInvitationResult> CreateInvitationAsync(
        DeviceRegistrySettings settings,
        string role,
        IReadOnlyList<DeviceInvitationAccount> accounts,
        IReadOnlyCollection<string> capabilities,
        CancellationToken cancellationToken = default)
    {
        if (!settings.IsConfigured || settings.Role != "Admin" || settings.IsExpired)
            return new DeviceInvitationResult(false, Detail: "An active Admin identity is required to invite devices.");

        if (role is not ("Admin" or "TrustedOnline" or "TrustedOffline" or "Temporary"))
            return new DeviceInvitationResult(false, Detail: "Invalid device role.");

        DeviceInvitationAccount[] selected = role == "Admin"
            ? []
            : accounts.GroupBy(account => account.SteamId, StringComparer.Ordinal).Select(group => group.First()).ToArray();
        if (role != "Admin" && selected.Length == 0)
            return new DeviceInvitationResult(false, Detail: "Select at least one account.");
        if (selected.Length > 64 || selected.Any(account => account.SteamId.Length != 17 || !account.SteamId.All(char.IsDigit)))
            return new DeviceInvitationResult(false, Detail: "The account grant is invalid.");
        if (role == "TrustedOffline" && selected.Any(account => account.MaFilePayload is null))
            return new DeviceInvitationResult(false, Detail: "Offline pairing requires the selected account payloads.");

        string[] grantedCapabilities = role == "Admin" ? [] : capabilities.Distinct(StringComparer.Ordinal).OrderBy(value => value, StringComparer.Ordinal).ToArray();
        string inviteId = Base64Url(RandomNumberGenerator.GetBytes(16));
        string inviteToken = Base64Url(RandomNumberGenerator.GetBytes(32));
        string verificationCode = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6", CultureInfo.InvariantCulture);
        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        long expiresAt = now + (long)TimeSpan.FromMinutes(5).TotalMilliseconds;
        long? deviceExpiresAt = role == "Temporary" ? now + (long)TimeSpan.FromHours(24).TotalMilliseconds : null;
        RemoteActionAuthorization authorization = new()
        {
            VaultId = settings.VaultId,
            InviteId = inviteId,
            IssuerDeviceId = signer.DeviceId,
            Role = role,
            IssuedAt = now,
            ExpiresAt = deviceExpiresAt,
            AccountSteamIds = selected.Select(account => account.SteamId).ToHashSet(StringComparer.Ordinal),
            Capabilities = grantedCapabilities.ToHashSet(StringComparer.Ordinal)
        };
        byte[] authorizationBytes = RemoteActionAuthorizationCodec.Canonical(authorization);
        try
        {
            JArray accountPayloads = [];
            int payloadBytes = 0;
            foreach (DeviceInvitationAccount account in selected.Where(account => account.MaFilePayload is not null))
            {
                payloadBytes += account.MaFilePayload!.Length;
                if (payloadBytes > 256 * 1024)
                    return new DeviceInvitationResult(false, Detail: "Selected account data is too large for one invitation.");
                accountPayloads.Add(new JObject
                {
                    ["steamId"] = account.SteamId,
                    ["maFile"] = Convert.ToBase64String(account.MaFilePayload)
                });
            }

            JObject grant = new()
            {
                ["version"] = 1,
                ["role"] = role,
                ["accountSteamIds"] = new JArray(selected.Select(account => account.SteamId).OrderBy(value => value, StringComparer.Ordinal)),
                ["capabilities"] = new JArray(grantedCapabilities),
                ["issuedAt"] = now,
                ["expiresAt"] = deviceExpiresAt,
                ["offlineWipeAfterHours"] = role == "TrustedOffline" ? 168 : null,
                ["accountPayloads"] = accountPayloads,
                ["adminDeviceId"] = signer.DeviceId,
                ["adminPublicKey"] = signer.PublicKey,
                ["adminEncryptionPublicKey"] = signer.EncryptionPublicKey,
                ["adminEncryptionKeySignature"] = signer.EncryptionKeySignature,
                ["remoteAuthorization"] = Base64Url(authorizationBytes),
                ["remoteAuthorizationSignature"] = signer.Sign(authorizationBytes)
            };
            byte[] grantBytes = Encoding.UTF8.GetBytes(grant.ToString(Formatting.None));
            string envelope = EncryptGrant(grantBytes, inviteToken, settings.VaultId, inviteId);
            JObject body = new()
            {
                ["inviteId"] = inviteId,
                ["tokenHash"] = Base64Url(SHA256.HashData(Encoding.UTF8.GetBytes(inviteToken))),
                ["verificationCodeHash"] = Base64Url(SHA256.HashData(Encoding.UTF8.GetBytes(verificationCode))),
                ["role"] = role,
                ["expiresAt"] = expiresAt,
                ["deviceExpiresAt"] = deviceExpiresAt,
                ["grantEnvelope"] = envelope,
                ["grantFingerprint"] = RemoteActionAuthorizationCodec.Fingerprint(authorizationBytes)
            };
            (bool success, _, string? detail) = await SendAsync(
                settings, $"/v1/trust/{settings.VaultId}/invites", HttpMethod.Post, body, cancellationToken);
            if (!success)
                return new DeviceInvitationResult(false, Detail: detail);

            string qr = $"sdapp://device-enroll?v=1&relay={Uri.EscapeDataString(settings.RelayUrl)}&vault={settings.VaultId}&invite={inviteId}&token={inviteToken}&exp={expiresAt}";
            return new DeviceInvitationResult(true, qr, verificationCode, expiresAt);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(authorizationBytes);
        }
    }

    public async Task<DeviceClaimResult> ClaimInvitationAsync(
        string qrPayload,
        string verificationCode,
        string displayName,
        CancellationToken cancellationToken = default)
    {
        if (!TryParseInvitation(qrPayload, out Uri? relay, out string vaultId, out string inviteId, out string inviteToken, out string? error))
            return new DeviceClaimResult(false, Detail: error);

        verificationCode = new string((verificationCode ?? string.Empty).Where(char.IsDigit).ToArray());
        if (verificationCode.Length != 6)
            return new DeviceClaimResult(false, Detail: "Enter the 6-digit device verification code.");

        displayName = string.IsNullOrWhiteSpace(displayName) ? Environment.MachineName : displayName.Trim();
        displayName = displayName[..Math.Min(displayName.Length, 64)];
        JObject body = new()
        {
            ["displayName"] = displayName,
            ["keyProtection"] = signer.Protection,
            ["verificationCode"] = verificationCode,
            ["encryptionPublicKey"] = signer.EncryptionPublicKey,
            ["encryptionKeySignature"] = signer.EncryptionKeySignature
        };
        string path = $"/v1/trust/{vaultId}/invites/{inviteId}/claim";
        (bool success, JObject? response, string? detail) = await SendSignedAsync(
            relay!, path, HttpMethod.Put, body, includePublicKey: true, inviteToken, cancellationToken);
        if (!success || response is null)
            return new DeviceClaimResult(false, Detail: detail);

        byte[]? grantBytes = null;
        byte[]? authorizationBytes = null;
        List<GrantedAccountPayload> accountPayloads = [];
        try
        {
            grantBytes = DecryptGrant(response.Value<string>("grantEnvelope"), inviteToken, vaultId, inviteId);
            JObject grant = JObject.Parse(Encoding.UTF8.GetString(grantBytes));
            if (grant.Value<int>("version") != 1)
                throw new CryptographicException("The invitation grant version is not supported.");

            string role = grant.Value<string>("role") ?? string.Empty;
            string adminDeviceId = grant.Value<string>("adminDeviceId") ?? string.Empty;
            string adminPublicKey = grant.Value<string>("adminPublicKey") ?? string.Empty;
            string adminEncryptionPublicKey = grant.Value<string>("adminEncryptionPublicKey") ?? string.Empty;
            string adminEncryptionKeySignature = grant.Value<string>("adminEncryptionKeySignature") ?? string.Empty;
            string authorizationText = grant.Value<string>("remoteAuthorization") ?? string.Empty;
            string authorizationSignature = grant.Value<string>("remoteAuthorizationSignature") ?? string.Empty;
            authorizationBytes = Base64UrlDecode(authorizationText);
            RemoteActionAuthorization authorization = RemoteActionAuthorizationCodec.Parse(authorizationBytes);
            HashSet<string> grantedIds = grant["accountSteamIds"]?.Values<string>().Where(item => item is not null).Select(item => item!).ToHashSet(StringComparer.Ordinal) ?? [];
            HashSet<string> grantedCapabilities = grant["capabilities"]?.Values<string>().Where(item => item is not null).Select(item => item!).ToHashSet(StringComparer.Ordinal) ?? [];
            RegistryDevice claimedDevice = response["device"]?.ToObject<RegistryDevice>()
                ?? throw new CryptographicException("The claimed device response is invalid.");

            if (claimedDevice.DeviceId != signer.DeviceId ||
                authorization.VaultId != vaultId || authorization.InviteId != inviteId ||
                authorization.IssuerDeviceId != adminDeviceId || authorization.Role != role ||
                authorization.Role != claimedDevice.Role || authorization.IssuedAt != grant.Value<long>("issuedAt") ||
                authorization.ExpiresAt != grant.Value<long?>("expiresAt") ||
                !authorization.AccountSteamIds.SetEquals(grantedIds) ||
                !authorization.Capabilities.SetEquals(grantedCapabilities))
                throw new CryptographicException("The remote action authorization does not match the invitation.");
            if (!VerifySignature(adminPublicKey, authorizationBytes, authorizationSignature))
                throw new CryptographicException("The remote action authorization signature is invalid.");
            if (!string.Equals(claimedDevice.GrantFingerprint, RemoteActionAuthorizationCodec.Fingerprint(authorizationBytes), StringComparison.Ordinal))
                throw new CryptographicException("The remote action authorization is not bound to this device.");
            if (!VerifyEncryptionKeyBinding(adminDeviceId, adminPublicKey, adminEncryptionPublicKey, adminEncryptionKeySignature))
                throw new CryptographicException("The Admin encryption identity is invalid.");

            int payloadBytes = 0;
            foreach (JObject payload in grant["accountPayloads"]?.Children<JObject>() ?? [])
            {
                string steamId = payload.Value<string>("steamId") ?? string.Empty;
                byte[] bytes = Convert.FromBase64String(payload.Value<string>("maFile") ?? string.Empty);
                payloadBytes += bytes.Length;
                if (!grantedIds.Contains(steamId) || bytes.Length == 0 || payloadBytes > 256 * 1024)
                {
                    CryptographicOperations.ZeroMemory(bytes);
                    throw new CryptographicException("The offline account payload is invalid.");
                }
                accountPayloads.Add(new GrantedAccountPayload(steamId, bytes));
            }
            if (role == "TrustedOffline" && !accountPayloads.Select(item => item.SteamId).ToHashSet(StringComparer.Ordinal).SetEquals(grantedIds))
                throw new CryptographicException("The offline grant does not contain every selected account.");
            if (role != "TrustedOffline" && accountPayloads.Count != 0)
                throw new CryptographicException("This device role cannot receive offline account data.");

            DeviceRegistrySettings settings = new()
            {
                VaultId = vaultId,
                RelayUrl = relay!.GetLeftPart(UriPartial.Authority),
                Role = role,
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
            return new DeviceClaimResult(true, settings, accountPayloads);
        }
        catch (Exception ex) when (ex is CryptographicException or JsonException or FormatException or ArgumentException)
        {
            foreach (GrantedAccountPayload payload in accountPayloads)
                CryptographicOperations.ZeroMemory(payload.MaFilePayload);
            return new DeviceClaimResult(false, Detail: ex.Message);
        }
        finally
        {
            if (grantBytes is not null) CryptographicOperations.ZeroMemory(grantBytes);
            if (authorizationBytes is not null) CryptographicOperations.ZeroMemory(authorizationBytes);
        }
    }

    private async Task<(bool Success, JObject? Json, string? Detail)> SendAsync(
        DeviceRegistrySettings settings,
        string path,
        HttpMethod method,
        JObject? json,
        CancellationToken cancellationToken)
    {
        if (!settings.IsConfigured || string.IsNullOrWhiteSpace(settings.RelayUrl))
            return (false, null, "The device registry is not configured.");

        return await SendSignedAsync(new Uri(settings.RelayUrl.TrimEnd('/')), path, method, json, false, null, cancellationToken);
    }

    private async Task<(bool Success, JObject? Json, string? Detail)> SendSignedAsync(
        Uri endpoint,
        string path,
        HttpMethod method,
        JObject? json,
        bool includePublicKey,
        string? bearerToken,
        CancellationToken cancellationToken)
    {
        byte[] body = json is null ? [] : Encoding.UTF8.GetBytes(json.ToString(Formatting.None));
        long timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        string nonce = Base64Url(RandomNumberGenerator.GetBytes(16));
        byte[] canonical = CreateCanonicalRequest(method.Method, path, timestamp, nonce, body);
        using HttpRequestMessage request = new(method, endpoint.GetLeftPart(UriPartial.Authority).TrimEnd('/') + path);
        request.Headers.Add("x-sdapp-device-id", signer.DeviceId);
        request.Headers.Add("x-sdapp-timestamp", timestamp.ToString(CultureInfo.InvariantCulture));
        request.Headers.Add("x-sdapp-nonce", nonce);
        request.Headers.Add("x-sdapp-signature", signer.Sign(canonical));
        if (includePublicKey)
            request.Headers.Add("x-sdapp-public-key", signer.PublicKey);
        if (!string.IsNullOrWhiteSpace(bearerToken))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);
        if (method != HttpMethod.Get)
        {
            request.Content = new ByteArrayContent(body);
            request.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
        }

        try
        {
            using HttpResponseMessage response = await httpClient.SendAsync(request, cancellationToken);
            string text = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                string detail = $"Registry HTTP {(int)response.StatusCode}";
                try { detail = JObject.Parse(text).Value<string>("error") ?? detail; }
                catch (JsonException) { }
                return (false, null, detail);
            }

            JObject responseJson = string.IsNullOrWhiteSpace(text) ? new JObject() : JObject.Parse(text);
            return (true, responseJson, null);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            return (false, null, ex.Message);
        }
    }

    public static byte[] CreateCanonicalRequest(string method, string path, long timestamp, string nonce, byte[] body)
    {
        string hash = Base64Url(SHA256.HashData(body));
        StringBuilder value = new("sdapp-device-request/v1\n");
        AddField(value, "method", method.ToUpperInvariant());
        AddField(value, "path", path);
        AddField(value, "timestamp", timestamp.ToString(CultureInfo.InvariantCulture));
        AddField(value, "nonce", nonce);
        AddField(value, "bodyHash", hash);
        return Encoding.UTF8.GetBytes(value.ToString());
    }

    private static void AddField(StringBuilder builder, string name, string value) =>
        builder.Append(name).Append('=').Append(Encoding.UTF8.GetByteCount(value)).Append(':').Append(value).Append('\n');

    private static string Base64Url(byte[] value) =>
        Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static string EncryptGrant(byte[] plain, string token, string vaultId, string inviteId)
    {
        byte[] iv = RandomNumberGenerator.GetBytes(12);
        byte[] key = SHA256.HashData(Encoding.UTF8.GetBytes($"sdapp-invite-grant-key/v1\n{token}\n"));
        byte[] cipher = new byte[plain.Length];
        byte[] tag = new byte[16];
        try
        {
            using AesGcm aes = new(key, 16);
            aes.Encrypt(iv, plain, cipher, tag, Encoding.UTF8.GetBytes($"sdapp-invite-grant/v1\n{vaultId}\n{inviteId}\n"));
            return Base64Url(iv.Concat(cipher).Concat(tag).ToArray());
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
            CryptographicOperations.ZeroMemory(plain);
        }
    }

    private static bool TryParseInvitation(
        string payload,
        out Uri? relay,
        out string vaultId,
        out string inviteId,
        out string inviteToken,
        out string? error)
    {
        relay = null;
        vaultId = inviteId = inviteToken = string.Empty;
        error = null;
        if (string.IsNullOrEmpty(payload) || payload.Length > 8192 ||
            !Uri.TryCreate(payload, UriKind.Absolute, out Uri? uri) || uri.Scheme != "sdapp" || uri.Host != "device-enroll" ||
            !string.IsNullOrEmpty(uri.UserInfo) || !uri.IsDefaultPort ||
            uri.AbsolutePath is not ("" or "/") || !string.IsNullOrEmpty(uri.Fragment))
        {
            error = "Invalid SDA++ device invitation.";
            return false;
        }
        Dictionary<string, string> query = new(StringComparer.Ordinal);
        foreach (string field in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            string[] pair = field.Split('=', 2);
            if (pair.Length != 2 || !query.TryAdd(Uri.UnescapeDataString(pair[0]), Uri.UnescapeDataString(pair[1])))
            {
                error = "The SDA++ device invitation contains invalid or duplicate parameters.";
                return false;
            }
        }
        if (!query.TryGetValue("v", out string? version) || version != "1" ||
            !query.TryGetValue("relay", out string? relayText) || !Uri.TryCreate(relayText, UriKind.Absolute, out relay) ||
            relay.Scheme != Uri.UriSchemeHttps || !string.IsNullOrEmpty(relay.Query) || !string.IsNullOrEmpty(relay.Fragment) ||
            !string.IsNullOrEmpty(relay.UserInfo) || relay.AbsolutePath != "/" ||
            !query.TryGetValue("vault", out string? parsedVault) || !IdPattern.IsMatch(parsedVault) ||
            !query.TryGetValue("invite", out string? parsedInvite) || !IdPattern.IsMatch(parsedInvite) ||
            !query.TryGetValue("token", out string? parsedToken) || !IsCanonicalInviteToken(parsedToken) ||
            !query.TryGetValue("exp", out string? expiryText) || !long.TryParse(expiryText, out long expiry) || expiry <= DateTimeOffset.UtcNow.ToUnixTimeMilliseconds())
        {
            error = "The SDA++ device invitation is invalid or expired.";
            return false;
        }
        vaultId = parsedVault;
        inviteId = parsedInvite;
        inviteToken = parsedToken;
        return true;
    }

    private static bool IsCanonicalInviteToken(string token)
    {
        if (token.Length != 43 || token.Any(character => !char.IsAsciiLetterOrDigit(character) && character is not ('-' or '_')))
            return false;
        byte[] decoded = Base64UrlDecode(token);
        try
        {
            return decoded.Length == 32 && Base64Url(decoded) == token;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(decoded);
        }
    }

    private static byte[] DecryptGrant(string? envelope, string token, string vaultId, string inviteId)
    {
        byte[] packed = Base64UrlDecode(envelope ?? string.Empty);
        if (packed.Length < 29 || packed.Length > 512 * 1024)
            throw new CryptographicException("The invitation grant is invalid.");
        byte[] key = SHA256.HashData(Encoding.UTF8.GetBytes($"sdapp-invite-grant-key/v1\n{token}\n"));
        byte[] plain = new byte[packed.Length - 28];
        try
        {
            using AesGcm aes = new(key, 16);
            aes.Decrypt(packed.AsSpan(0, 12), packed.AsSpan(12, plain.Length), packed.AsSpan(packed.Length - 16), plain,
                Encoding.UTF8.GetBytes($"sdapp-invite-grant/v1\n{vaultId}\n{inviteId}\n"));
            return plain;
        }
        catch
        {
            CryptographicOperations.ZeroMemory(plain);
            throw;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
            CryptographicOperations.ZeroMemory(packed);
        }
    }

    private static bool VerifyEncryptionKeyBinding(string deviceId, string publicKey, string encryptionPublicKey, string signature)
    {
        byte[] publicBytes = Base64UrlDecode(publicKey);
        byte[] digest = SHA256.HashData(publicBytes);
        try
        {
            if (deviceId != Base64Url(digest[..16])) return false;
            StringBuilder builder = new("sdapp-device-encryption-key/v1\n");
            AddField(builder, "deviceId", deviceId);
            AddField(builder, "publicKey", encryptionPublicKey);
            return VerifySignature(publicKey, Encoding.UTF8.GetBytes(builder.ToString()), signature);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(publicBytes);
            CryptographicOperations.ZeroMemory(digest);
        }
    }

    private static bool VerifySignature(string publicKey, byte[] payload, string signature)
    {
        byte[] publicBytes = Base64UrlDecode(publicKey);
        byte[] signatureBytes = Base64UrlDecode(signature);
        try
        {
            using ECDsa verifier = ECDsa.Create();
            verifier.ImportSubjectPublicKeyInfo(publicBytes, out int read);
            return read == publicBytes.Length && verifier.VerifyData(payload, signatureBytes, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(publicBytes);
            CryptographicOperations.ZeroMemory(signatureBytes);
        }
    }

    private static byte[] Base64UrlDecode(string value)
    {
        string padded = value.Replace('-', '+').Replace('_', '/');
        padded += new string('=', (4 - padded.Length % 4) % 4);
        return Convert.FromBase64String(padded);
    }
}
