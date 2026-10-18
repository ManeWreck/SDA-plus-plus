using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace SDAPlusPlus.Core.Devices;

public sealed class DeviceRegistryClient(HttpClient httpClient, IDeviceRequestSigner signer,
    Action<DeviceRegistrySettings>? persist = null, DeviceResultOutbox? resultOutbox = null,
    AdminResultInbox? resultInbox = null) : IDeviceRegistryClient
{
    public string RecipientCode => EndToEndTrust.RecipientCode(signer);
    public Task<DeviceInvitationResult> CreateInvitationAsync(DeviceRegistrySettings settings, string role,
        IReadOnlyList<DeviceInvitationAccount> accounts, IReadOnlyCollection<string> capabilities, CancellationToken cancellationToken = default)
        => Task.FromResult(new DeviceInvitationResult(false, Detail: "Legacy enrollment disabled. Obtain the recipient identity directly first."));

    public async Task<DeviceInvitationResult> CreateBoundInvitationAsync(DeviceRegistrySettings settings, string recipientCode, string role,
        IReadOnlyList<DeviceInvitationAccount> accounts, IReadOnlyCollection<string> capabilities, CancellationToken cancellationToken = default)
    {
        if (!settings.IsConfigured || settings.Role != "Admin" || settings.IsExpired || persist is null)
            return new(false, Detail: "Active Admin and durable local trust store required.");
        try
        {
            var recipient = EndToEndTrust.ParseRecipient(recipientCode);
            if (recipient.DeviceId == signer.DeviceId) return new(false, Detail: "Cannot invite this device.");
            long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), expiry = now + 300_000;
            string invite = EndToEndTrust.Encode(RandomNumberGenerator.GetBytes(16)), token = EndToEndTrust.Encode(RandomNumberGenerator.GetBytes(32));
            string code = RandomNumberGenerator.GetInt32(1_000_000).ToString("D6");
            long revision = settings.IssuedGrants.TryGetValue(recipient.DeviceId, out var old)
                ? checked(EndToEndTrust.Verify<DeviceGrantV2>(old, signer.PublicKey).Revision + 1) : 1;
            var grant = new DeviceGrantV2(2, settings.VaultId, invite, signer.DeviceId, recipient.DeviceId,
                recipient.PublicKey, recipient.EncryptionPublicKey, role, capabilities.Order(StringComparer.Ordinal).ToArray(),
                accounts.Select(x => x.SteamId).Order(StringComparer.Ordinal).ToArray(), now,
                now + (role == "Temporary" ? 86_400_000L : 30L * 86_400_000), Guid.NewGuid().ToString("D"), revision,
                role == "TrustedOffline" ? accounts.Select(x => new OfflineTrustAccount(x.SteamId,
                    Convert.ToBase64String(x.MaFilePayload ?? throw new CryptographicException()))).ToArray() : []);
            string signed = EndToEndTrust.Sign(grant, signer);
            EndToEndTrust.ValidateGrant(signed, signer.PublicKey, settings.VaultId, invite, recipient, now);
            if (Encoding.UTF8.GetByteCount(signed) > 256 * 1024) return new(false, Detail: "Grant too large.");
            string encrypted = EndToEndTrust.EncryptGrant(signed, recipient.EncryptionPublicKey, settings.VaultId, invite);
            // Commit local authority before publishing. Relay state is never the authority.
            settings.IssuedGrants[recipient.DeviceId] = signed; settings.RevokedDevices.Remove(recipient.DeviceId); persist(settings);
            var body = new JObject { ["inviteId"] = invite, ["tokenHash"] = EndToEndTrust.Fingerprint(token),
                ["verificationCodeHash"] = EndToEndTrust.Fingerprint(code), ["role"] = role, ["expiresAt"] = expiry,
                ["deviceExpiresAt"] = role == "Temporary" ? grant.ExpiresAt : null, ["grantEnvelope"] = encrypted,
                ["grantFingerprint"] = EndToEndTrust.Fingerprint(signed) };
            var result = await Send(settings, $"/v1/trust/{settings.VaultId}/invites", HttpMethod.Post, body, cancellationToken);
            if (!result.Success) return new(false, Detail: result.Detail);
            string qr = $"sdapp://device-enroll?v=2&relay={Uri.EscapeDataString(settings.RelayUrl)}&vault={settings.VaultId}&invite={invite}&token={token}&exp={expiry}&admin={signer.PublicKey}&recipient={recipient.DeviceId}&grant={grant.GrantId}&revision={revision}";
            return new(true, qr, code, expiry);
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException or ArgumentException or JsonException or System.Text.Json.JsonException or IOException or OverflowException)
        { return new(false, Detail: "Cannot create E2E invitation. Check recipient identity and local storage."); }
    }

    public async Task<DeviceClaimResult> ClaimInvitationAsync(string qrPayload, string verificationCode, string displayName, CancellationToken cancellationToken = default)
    {
        try
        {
            var q = ParseInvitation(qrPayload);
            if (q["recipient"] != signer.DeviceId || verificationCode.Length != 6 || !verificationCode.All(char.IsAsciiDigit)) return new(false, Detail: "Invalid recipient or code.");
            var own = new RecipientIdentity(signer.DeviceId, signer.PublicKey, signer.EncryptionPublicKey, signer.EncryptionKeySignature);
            var body = new JObject { ["displayName"] = displayName, ["keyProtection"] = signer.Protection, ["verificationCode"] = verificationCode,
                ["encryptionPublicKey"] = signer.EncryptionPublicKey, ["encryptionKeySignature"] = signer.EncryptionKeySignature };
            var response = await SendSigned(q["relay"], $"/v1/trust/{q["vault"]}/invites/{q["invite"]}/claim", HttpMethod.Put, body, cancellationToken, q["token"], true);
            if (!response.Success) return new(false, Detail: response.Detail);
            string signed = EndToEndTrust.DecryptGrant(response.Json!.Value<string>("grantEnvelope")!, signer, q["vault"], q["invite"]);
            var grant = EndToEndTrust.ValidateGrant(signed, q["admin"], q["vault"], q["invite"], own, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
            if (grant.GrantId != q["grant"] || grant.Revision.ToString(System.Globalization.CultureInfo.InvariantCulture) != q["revision"]) throw new CryptographicException();
            // Role and anchor come from the verified grant and OOB QR, never the relay response.
            var settings = new DeviceRegistrySettings { VaultId = grant.VaultId, RelayUrl = q["relay"], Role = grant.Role, ExpiresAt = grant.ExpiresAt,
                GrantedAccountSteamIds = grant.SteamIds.ToHashSet(), Capabilities = grant.Capabilities.ToHashSet(),
                AdminDeviceId = grant.IssuerDeviceId, AdminPublicKey = q["admin"], E2EGrant = signed };
            return new(true, settings, grant.Accounts.Select(a => new GrantedAccountPayload(a.SteamId, Convert.FromBase64String(a.MaFile))).ToArray());
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException or ArgumentException or JsonException or System.Text.Json.JsonException or KeyNotFoundException or NullReferenceException or NotSupportedException)
        { return new(false, Detail: "E2E verification failed. Legacy v1 is not accepted."); }
    }
    private static Dictionary<string, string> ParseInvitation(string text)
    {
        if (text.Length > 8192 || !Uri.TryCreate(text, UriKind.Absolute, out var uri) || uri.Scheme != "sdapp" || uri.Host != "device-enroll" ||
            uri.AbsolutePath is not ("" or "/") || uri.Fragment != "" || uri.UserInfo != "" || !uri.IsDefaultPort) throw new FormatException();
        var q = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var field in uri.Query.TrimStart('?').Split('&'))
        { var parts = field.Split('=', 2); if (parts.Length != 2 || !q.TryAdd(Uri.UnescapeDataString(parts[0]), Uri.UnescapeDataString(parts[1]))) throw new FormatException(); }
        if (q.Count != 10 || q["v"] != "2" || EndToEndTrust.Decode(q["token"]).Length != 32 ||
            EndToEndTrust.Decode(q["vault"]).Length != 16 || EndToEndTrust.Decode(q["invite"]).Length != 16 ||
            !long.TryParse(q["exp"], out long exp) || exp <= DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() ||
            !Uri.TryCreate(q["relay"], UriKind.Absolute, out var relay) || relay.Scheme != "https" || relay.UserInfo != "" ||
            relay.AbsolutePath != "/" || relay.Query != "" || relay.Fragment != "") throw new FormatException();
        return q;
    }
    private DeviceGrantV2 LocalGrant(DeviceRegistrySettings settings) => EndToEndTrust.ValidateGrant(settings.E2EGrant, settings.AdminPublicKey,
        settings.VaultId, EndToEndTrust.Verify<DeviceGrantV2>(settings.E2EGrant, settings.AdminPublicKey).InviteId,
        new(signer.DeviceId, signer.PublicKey, signer.EncryptionPublicKey, signer.EncryptionKeySignature), DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
    public async Task<PendingDeviceCommandsResult> PendingCommandsAsync(DeviceRegistrySettings settings, CancellationToken cancellationToken = default)
    {
        try
        {
            var grant = LocalGrant(settings);
            var result = await Send(settings, $"/v1/trust/{settings.VaultId}/commands", HttpMethod.Get, null, cancellationToken);
            if (!result.Success || result.Json?["commands"] is not JArray entries || entries.Count > 100) return new(false, [], result.Detail);
            var commands = new List<IncomingDeviceCommand>(); var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (JObject item in entries)
            {
                string envelope = item.Value<string>("signedEnvelope") ?? throw new CryptographicException();
                var c = EndToEndTrust.ValidateCommand(envelope, settings.AdminPublicKey, grant, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
                if (item.Value<string>("targetDeviceId") != c.TargetDeviceId || item.Value<string>("commandId") != c.CommandId ||
                    item.Value<string>("type") != c.CommandType || item.Value<long>("expiresAt") != c.ExpiresAt || !ids.Add(c.CommandId)) throw new CryptographicException();
                commands.Add(new(c.VaultId, c.TargetDeviceId, c.CommandId, c.CommandType, c.ExpiresAt) { SignedEnvelope = envelope });
            }
            return new(true, commands);
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException or ArgumentException or JsonException or System.Text.Json.JsonException or NullReferenceException or InvalidCastException)
        { return new(false, [], "Unverified/legacy command batch rejected. E2E v2 required."); }
    }
    public async Task<DeviceCommandResult> IssueCommandAsync(DeviceRegistrySettings settings, string deviceId, string commandType, CancellationToken cancellationToken = default)
    {
        if (persist is null || settings.Role != "Admin" || settings.IsExpired || settings.RevokedDevices.Contains(deviceId) || !settings.IssuedGrants.TryGetValue(deviceId, out var signed)) return new(false, "Durable store and locally issued active grant required.");
        var grant = EndToEndTrust.Verify<DeviceGrantV2>(signed, signer.PublicKey); long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var command = new DeviceCommandV2(2, settings.VaultId, Guid.NewGuid().ToString("D"), signer.DeviceId, deviceId, commandType,
            now, Math.Min(now + 86_400_000, grant.ExpiresAt), "ManageDevices", grant.GrantId, grant.Revision);
        var envelope = EndToEndTrust.Sign(command, signer); EndToEndTrust.ValidateCommand(envelope, signer.PublicKey, grant, now);
        settings.IssuedCommands.Add(command.CommandId, new(envelope, grant.RecipientPublicKey));
        persist(settings);
        var r = await Send(settings, $"/v1/trust/{settings.VaultId}/devices/{deviceId}/commands", HttpMethod.Post,
            new JObject { ["commandId"] = command.CommandId, ["type"] = commandType, ["expiresAt"] = command.ExpiresAt, ["signedEnvelope"] = envelope }, cancellationToken);
        return new(r.Success, r.Detail);
    }
    public string SignFreshness(DeviceRegistrySettings settings, string deviceId, string nonce)
    {
        if (settings.Role != "Admin" || settings.IsExpired || EndToEndTrust.Decode(nonce).Length != 32 || !settings.IssuedGrants.TryGetValue(deviceId, out var signed)) throw new CryptographicException();
        var grant = EndToEndTrust.Verify<DeviceGrantV2>(signed, signer.PublicKey); long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        if (grant.VaultId != settings.VaultId || grant.RecipientDeviceId != deviceId || grant.ExpiresAt <= now) throw new CryptographicException();
        return EndToEndTrust.Sign(new TrustFreshnessV2(2, settings.VaultId, signer.DeviceId, deviceId, grant.GrantId, grant.Revision,
            nonce, now, now + 60_000, settings.RevokedDevices.Contains(deviceId)), signer);
    }
    public async Task<DeviceCommandResult> PublishFreshnessChallengeAsync(DeviceRegistrySettings settings, TrustChallenge challenge, CancellationToken ct = default)
    {
        LocalGrant(settings);
        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        if (challenge.CreatedAt > now || now - challenge.CreatedAt >= 60_000)
            return new(false, "Challenge expired.");
        var result = await Send(settings, $"/v1/trust/{settings.VaultId}/freshness", HttpMethod.Post,
            new JObject { ["nonce"] = challenge.Nonce }, ct);
        return new(result.Success, result.Detail);
    }
    // This is transport only. VerifyAndSimulateAsync must validate and consume the receiver-owned challenge.
    public async Task<string?> FetchUnverifiedFreshnessProofAsync(DeviceRegistrySettings settings, TrustChallenge challenge, CancellationToken ct = default)
    {
        var result = await Send(settings, $"/v1/trust/{settings.VaultId}/freshness/{signer.DeviceId}/{challenge.Nonce}", HttpMethod.Get, null, ct);
        if (!result.Success) return null;
        var proof = result.Json?.Value<string>("signedProof");
        if (proof is { Length: > 16000 }) throw new CryptographicException("Oversized freshness proof.");
        return proof;
    }
    public async Task<int> RespondToFreshnessChallengesAsync(DeviceRegistrySettings settings, CancellationToken ct = default)
    {
        if (settings.Role != "Admin" || settings.IsExpired) throw new CryptographicException("Active local Admin required.");
        var response = await Send(settings, $"/v1/trust/{settings.VaultId}/freshness", HttpMethod.Get, null, ct);
        if (!response.Success) throw new HttpRequestException(response.Detail);
        if (response.Json?["challenges"] is not JArray challenges || challenges.Count > 100)
            throw new CryptographicException("Invalid challenge batch.");
        int answered = 0;
        foreach (var item in challenges)
        {
            ct.ThrowIfCancellationRequested();
            string? device = item.Value<string>("deviceId"), nonce = item.Value<string>("nonce");
            // Relay identities are hints only. No local grant means no signature is issued.
            if (device is null || nonce is null || !settings.IssuedGrants.ContainsKey(device)) continue;
            if (EndToEndTrust.Decode(device).Length != 16 || EndToEndTrust.Decode(nonce).Length != 32)
                throw new CryptographicException("Invalid challenge identity.");
            string proof = SignFreshness(settings, device, nonce);
            var result = await Send(settings, $"/v1/trust/{settings.VaultId}/freshness/{device}/{nonce}", HttpMethod.Put,
                new JObject { ["signedProof"] = proof }, ct);
            if (result.Success) answered++;
        }
        return answered;
    }
    public async Task<DeviceCommandResult> AcknowledgeCommandAsync(DeviceRegistrySettings settings, DeviceExecutionReceipt receipt, CancellationToken cancellationToken = default)
    {
        if (receipt.Command.SignedEnvelope is null || receipt.Command.DeviceId != signer.DeviceId || receipt.Command.VaultId != settings.VaultId ||
            receipt.State is not (DeviceExecutionState.Completed or DeviceExecutionState.Failed or DeviceExecutionState.OutcomeUnknown)) return new(false, "Signed command and final receipt required.");
        if (resultOutbox is null) return new(false, "Durable result outbox required.");
        QueuedDeviceResult queued;
        try
        {
            queued = await resultOutbox.EnqueueAsync(receipt, settings.AdminPublicKey, cancellationToken);
        }
        catch (Exception e) when (e is CryptographicException or FormatException or System.Text.Json.JsonException)
        { return new(false, "Invalid signed receipt context."); }
        var result = await SendQueuedResult(settings, queued, cancellationToken);
        return new(result.Success, result.Detail);
    }
    private Task<(bool Success, JObject? Json, string? Detail)> SendQueuedResult(DeviceRegistrySettings settings, QueuedDeviceResult queued, CancellationToken ct)
        => Send(settings, $"/v1/trust/{settings.VaultId}/commands/{queued.Receipt.Command.CommandId}/ack", HttpMethod.Post,
            new JObject { ["status"] = queued.Receipt.State.ToString(), ["signedResult"] = queued.SignedResult }, ct);
    public Task<int> RetryAcknowledgmentsAsync(DeviceRegistrySettings settings, CancellationToken ct = default)
        => (resultOutbox ?? throw new InvalidOperationException("Durable result outbox required.")).RetryAsync(
            settings.VaultId, settings.AdminPublicKey, async (item, token) => (await SendQueuedResult(settings, item, token)).Success, ct);
    public DeviceResultV2 VerifyCommandResult(DeviceRegistrySettings settings, string commandId, string signedResult)
    {
        if (settings.Role != "Admin" || !settings.IssuedCommands.TryGetValue(commandId, out var issued))
            throw new CryptographicException("Unknown locally issued command.");
        if (EndToEndTrust.Verify<DeviceCommandV2>(issued.Envelope, signer.PublicKey).CommandId != commandId)
            throw new CryptographicException("Local command archive mismatch.");
        return AdminCommandResultVerifier.Verify(issued, signedResult, signer.PublicKey, settings.VaultId, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
    }
    public Task<AdminResultAcceptance> AcceptCommandResultAsync(DeviceRegistrySettings settings, string commandId, string signedResult, CancellationToken ct = default)
    {
        if (resultInbox is null || settings.Role != "Admin" || !settings.IssuedCommands.TryGetValue(commandId, out var issued))
            throw new CryptographicException("Durable inbox and locally issued command required.");
        var original = EndToEndTrust.Verify<DeviceCommandV2>(issued.Envelope, signer.PublicKey);
        if (original.CommandId != commandId) throw new CryptographicException("Local command archive mismatch.");
        return resultInbox.AcceptAsync(issued, signedResult, signer.PublicKey, settings.VaultId, ct);
    }
    public async Task<DeviceCommandResult> RevokeAsync(DeviceRegistrySettings settings, string deviceId, CancellationToken cancellationToken = default)
    {
        if (!settings.IsConfigured || settings.Role != "Admin" || settings.IsExpired || deviceId == signer.DeviceId) return new(false, "Admin required.");
        if (settings.IssuedGrants.ContainsKey(deviceId)) { if (persist is null) return new(false, "Durable trust store required."); settings.RevokedDevices.Add(deviceId); persist(settings); }
        var r = await Send(settings, $"/v1/trust/{settings.VaultId}/devices/{Uri.EscapeDataString(deviceId)}", HttpMethod.Delete, null, cancellationToken); return new(r.Success, r.Detail);
    }
    public async Task<DeviceListResult> ListDevicesAsync(DeviceRegistrySettings settings, CancellationToken cancellationToken = default)
    { var r = await Send(settings, $"/v1/trust/{settings.VaultId}/devices", HttpMethod.Get, null, cancellationToken); return new(r.Success, r.Json?["devices"]?.ToObject<List<RegistryDevice>>() ?? [], r.Detail); }
    public async Task<DeviceAuditResult> ListAuditAsync(DeviceRegistrySettings settings, CancellationToken cancellationToken = default)
    { var r = await Send(settings, $"/v1/trust/{settings.VaultId}/audit", HttpMethod.Get, null, cancellationToken); return new(r.Success, r.Json?["events"]?.ToObject<List<RegistryAuditEvent>>() ?? [], r.Detail); }
    private Task<(bool Success, JObject? Json, string? Detail)> Send(DeviceRegistrySettings settings, string path, HttpMethod method, JObject? body, CancellationToken ct) => SendSigned(settings.RelayUrl, path, method, body, ct);
    private async Task<(bool Success, JObject? Json, string? Detail)> SendSigned(string relay, string path, HttpMethod method, JObject? json, CancellationToken ct, string? token = null, bool publicKey = false)
    {
        byte[] body = json is null ? [] : Encoding.UTF8.GetBytes(json.ToString(Formatting.None));
        long timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds(); string nonce = EndToEndTrust.Encode(RandomNumberGenerator.GetBytes(16));
        using var request = new HttpRequestMessage(method, relay.TrimEnd('/') + path);
        request.Headers.Add("x-sdapp-device-id", signer.DeviceId); request.Headers.Add("x-sdapp-timestamp", timestamp.ToString(System.Globalization.CultureInfo.InvariantCulture));
        request.Headers.Add("x-sdapp-nonce", nonce); request.Headers.Add("x-sdapp-signature", signer.Sign(CreateCanonicalRequest(method.Method, path, timestamp, nonce, body)));
        if (token is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (publicKey) request.Headers.Add("x-sdapp-public-key", signer.PublicKey);
        if (method != HttpMethod.Get) { request.Content = new ByteArrayContent(body); request.Content.Headers.ContentType = new("application/json"); }
        try
        {
            using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            if (!response.IsSuccessStatusCode) return (false, null, $"Registry HTTP {(int)response.StatusCode}");
            await response.Content.LoadIntoBufferAsync(1_000_000);
            string text = await response.Content.ReadAsStringAsync(ct);
            return (true, string.IsNullOrWhiteSpace(text) ? new JObject() : JObject.Parse(text), null);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException) { return (false, null, "Registry unavailable."); }
    }
    public static byte[] CreateCanonicalRequest(string method, string path, long timestamp, string nonce, byte[] body)
    {
        var b = new StringBuilder("sdapp-device-request/v1\n");
        foreach (var (name, value) in new[] { ("method", method.ToUpperInvariant()), ("path", path), ("timestamp", timestamp.ToString(System.Globalization.CultureInfo.InvariantCulture)), ("nonce", nonce), ("bodyHash", EndToEndTrust.Encode(SHA256.HashData(body))) })
            b.Append(name).Append('=').Append(Encoding.UTF8.GetByteCount(value)).Append(':').Append(value).Append('\n');
        return Encoding.UTF8.GetBytes(b.ToString());
    }
}
