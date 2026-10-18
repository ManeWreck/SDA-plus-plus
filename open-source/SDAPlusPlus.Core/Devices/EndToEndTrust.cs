using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SDAPlusPlus.Core.Devices;

public sealed record RecipientIdentity(string DeviceId, string PublicKey, string EncryptionPublicKey, string EncryptionKeySignature);
public sealed record OfflineTrustAccount(string SteamId, string MaFile);
public sealed record DeviceGrantV2(int Version, string VaultId, string InviteId, string IssuerDeviceId,
    string RecipientDeviceId, string RecipientPublicKey, string RecipientEncryptionPublicKey, string Role,
    string[] Capabilities, string[] SteamIds, long IssuedAt, long ExpiresAt, string GrantId, long Revision,
    OfflineTrustAccount[] Accounts);
public sealed record DeviceCommandV2(int Version, string VaultId, string CommandId, string IssuerDeviceId,
    string TargetDeviceId, string CommandType, long IssuedAt, long ExpiresAt, string RequiredCapability,
    string GrantId, long TrustRevision);
public sealed record DeviceResultV2(int Version, string VaultId, string CommandId, string CommandDigest,
    string IssuerDeviceId, string TargetDeviceId, string Result, long IssuedAt, string GrantId, long TrustRevision);
public sealed record TrustFreshnessV2(int Version, string VaultId, string IssuerDeviceId, string TargetDeviceId,
    string GrantId, long TrustRevision, string Nonce, long IssuedAt, long ExpiresAt, bool Revoked);
public sealed record SignedTrustEnvelope(string Payload, string Signature);
public sealed record EncryptedTrustEnvelope(string WrappedKey, string Nonce, string Ciphertext, string Tag);

// An unpredictable, receiver-owned, single-use challenge. Never construct it from relay data.
public sealed class TrustChallenge
{
    public string Nonce { get; } = EndToEndTrust.Encode(RandomNumberGenerator.GetBytes(32));
    public long CreatedAt { get; }
    private int consumed;
    public TrustChallenge(long now) => CreatedAt = now;
    internal bool Consume(long now) => now >= CreatedAt && now - CreatedAt <= 60_000 &&
        Interlocked.CompareExchange(ref consumed, 1, 0) == 0;
}

public static class EndToEndTrust
{
    private static readonly JsonSerializerOptions Json = new()
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };
    public static string Encode(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    public static byte[] Decode(string value)
    {
        Require(value is { Length: > 0 and <= 1_000_000 });
        var bytes = Convert.FromBase64String(value.Replace('-', '+').Replace('_', '/') + new string('=', (4 - value.Length % 4) % 4));
        Require(Encode(bytes) == value);
        return bytes;
    }
    public static string DeviceId(string publicKey) => Encode(SHA256.HashData(Decode(publicKey))[..16]);
    public static string Fingerprint(string text) => Encode(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    private static void Require(bool condition) { if (!condition) throw new CryptographicException("Invalid E2E trust document."); }
    private static string Domain<T>() => typeof(T) == typeof(DeviceGrantV2) ? "sdapp-grant/v2" :
        typeof(T) == typeof(DeviceCommandV2) ? "sdapp-command/v2" : typeof(T) == typeof(DeviceResultV2) ? "sdapp-result/v2" :
        typeof(T) == typeof(TrustFreshnessV2) ? "sdapp-freshness/v2" : throw new CryptographicException("Unsupported signed type.");
    private static byte[] Canonical<T>(T value) => Encoding.UTF8.GetBytes(Domain<T>() + "\n" + JsonSerializer.Serialize(value, Json));
    public static string Sign<T>(T value, IDeviceRequestSigner signer) => JsonSerializer.Serialize(
        new SignedTrustEnvelope(Encode(Canonical(value)), signer.Sign(Canonical(value))), Json);
    public static T Verify<T>(string envelope, string pinnedPublicKey)
    {
        Require(envelope.Length <= 1_000_000);
        var signed = JsonSerializer.Deserialize<SignedTrustEnvelope>(envelope, Json)!;
        var bytes = Decode(signed.Payload);
        using var key = ECDsa.Create();
        var publicBytes = Decode(pinnedPublicKey);
        key.ImportSubjectPublicKeyInfo(publicBytes, out int read);
        Require(read == publicBytes.Length && key.KeySize == 256 && key.VerifyData(bytes, Decode(signed.Signature),
            HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence));
        var prefix = Encoding.UTF8.GetBytes(Domain<T>() + "\n");
        Require(bytes.AsSpan().StartsWith(prefix));
        var value = JsonSerializer.Deserialize<T>(bytes.AsSpan(prefix.Length), Json)!;
        Require(bytes.AsSpan().SequenceEqual(Canonical(value)));
        return value;
    }
    public static void ValidateRecipient(RecipientIdentity recipient)
    {
        Require(recipient.DeviceId == DeviceId(recipient.PublicKey));
        var binding = new StringBuilder("sdapp-device-encryption-key/v1\n");
        foreach (var (name, value) in new[] { ("deviceId", recipient.DeviceId), ("publicKey", recipient.EncryptionPublicKey) })
            binding.Append(name).Append('=').Append(Encoding.UTF8.GetByteCount(value)).Append(':').Append(value).Append('\n');
        using var ec = ECDsa.Create();
        ec.ImportSubjectPublicKeyInfo(Decode(recipient.PublicKey), out _);
        Require(ec.VerifyData(Encoding.UTF8.GetBytes(binding.ToString()), Decode(recipient.EncryptionKeySignature),
            HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence));
        using var rsa = RSA.Create();
        var raw = Decode(recipient.EncryptionPublicKey);
        rsa.ImportSubjectPublicKeyInfo(raw, out int read);
        Require(read == raw.Length && rsa.KeySize >= 2048);
    }
    public static string RecipientCode(IDeviceRequestSigner signer) => "sdapp-recipient-v2:" + Encode(JsonSerializer.SerializeToUtf8Bytes(
        new RecipientIdentity(signer.DeviceId, signer.PublicKey, signer.EncryptionPublicKey, signer.EncryptionKeySignature), Json));
    public static RecipientIdentity ParseRecipient(string code)
    {
        Require(code.StartsWith("sdapp-recipient-v2:", StringComparison.Ordinal) && code.Length < 8192);
        var recipient = JsonSerializer.Deserialize<RecipientIdentity>(Decode(code[19..]), Json)!;
        ValidateRecipient(recipient); return recipient;
    }
    public static string EncryptGrant(string signedGrant, string recipientEncryptionKey, string vault, string invite)
    {
        var key = RandomNumberGenerator.GetBytes(32);
        var nonce = RandomNumberGenerator.GetBytes(12);
        var plain = Encoding.UTF8.GetBytes(signedGrant);
        var cipher = new byte[plain.Length]; var tag = new byte[16];
        try
        {
            using var aes = new AesGcm(key, 16);
            aes.Encrypt(nonce, plain, cipher, tag, Encoding.UTF8.GetBytes($"sdapp-enrollment/v2\n{vault}\n{invite}"));
            using var rsa = RSA.Create(); rsa.ImportSubjectPublicKeyInfo(Decode(recipientEncryptionKey), out _);
            return Encode(JsonSerializer.SerializeToUtf8Bytes(new EncryptedTrustEnvelope(
                Encode(rsa.Encrypt(key, RSAEncryptionPadding.OaepSHA256)), Encode(nonce), Encode(cipher), Encode(tag)), Json));
        }
        finally { CryptographicOperations.ZeroMemory(key); CryptographicOperations.ZeroMemory(plain); }
    }
    public static string DecryptGrant(string envelope, IDeviceRequestSigner recipient, string vault, string invite)
    {
        var encrypted = JsonSerializer.Deserialize<EncryptedTrustEnvelope>(Decode(envelope), Json)!;
        var key = recipient.UnwrapTrustKey(Decode(encrypted.WrappedKey));
        var plain = new byte[Decode(encrypted.Ciphertext).Length];
        try
        {
            Require(key.Length == 32);
            using var aes = new AesGcm(key, 16);
            aes.Decrypt(Decode(encrypted.Nonce), Decode(encrypted.Ciphertext), Decode(encrypted.Tag), plain,
                Encoding.UTF8.GetBytes($"sdapp-enrollment/v2\n{vault}\n{invite}"));
            return Encoding.UTF8.GetString(plain);
        }
        finally { CryptographicOperations.ZeroMemory(key); CryptographicOperations.ZeroMemory(plain); }
    }
    public static DeviceGrantV2 ValidateGrant(string signed, string anchor, string vault, string invite,
        RecipientIdentity recipient, long now)
    {
        ValidateRecipient(recipient);
        var g = Verify<DeviceGrantV2>(signed, anchor);
        Require(g.Version == 2 && g.VaultId == vault && g.InviteId == invite && g.IssuerDeviceId == DeviceId(anchor) &&
            g.RecipientDeviceId == recipient.DeviceId && g.RecipientPublicKey == recipient.PublicKey &&
            g.RecipientEncryptionPublicKey == recipient.EncryptionPublicKey &&
            g.Role is "TrustedOnline" or "TrustedOffline" or "Temporary" &&
            g.IssuedAt > 0 && g.IssuedAt <= now && g.ExpiresAt > now && g.ExpiresAt > g.IssuedAt &&
            Guid.TryParseExact(g.GrantId, "D", out _) && g.Revision > 0 &&
            g.SteamIds.Length is > 0 and <= 64 && g.SteamIds.Distinct().Count() == g.SteamIds.Length &&
            g.SteamIds.All(id => id.Length == 17 && id.All(char.IsAsciiDigit)) &&
            g.Capabilities.Distinct().Count() == g.Capabilities.Length &&
            g.Capabilities.All(c => c is "ViewGuardCode" or "ApproveSteamQr" or "ViewConfirmations" or "ApproveConfirmations" or "TerminateSessions") &&
            g.Accounts.All(a => g.SteamIds.Contains(a.SteamId)));
        Require(g.Role == "TrustedOffline" ? g.Accounts.Select(a => a.SteamId).Order().SequenceEqual(g.SteamIds.Order()) : g.Accounts.Length == 0);
        return g;
    }
    public static DeviceCommandV2 ValidateCommand(string signed, string anchor, DeviceGrantV2 grant, long now)
    {
        var c = Verify<DeviceCommandV2>(signed, anchor);
        Require(c.Version == 2 && grant.IssuerDeviceId == DeviceId(anchor) && c.IssuerDeviceId == grant.IssuerDeviceId &&
            c.VaultId == grant.VaultId && c.TargetDeviceId == grant.RecipientDeviceId &&
            c.GrantId == grant.GrantId && c.TrustRevision == grant.Revision &&
            c.RequiredCapability == "ManageDevices" && c.CommandType is "Lock" or "WipeLocalData" or "TerminateSteamSessions" &&
            Guid.TryParseExact(c.CommandId, "D", out _) && c.CommandId == c.CommandId.ToLowerInvariant() &&
            c.IssuedAt >= grant.IssuedAt && c.IssuedAt <= now && c.ExpiresAt > now &&
            c.ExpiresAt > c.IssuedAt && c.ExpiresAt - c.IssuedAt <= 86_400_000 && c.ExpiresAt <= grant.ExpiresAt);
        return c;
    }
    public static void ValidateFreshness(string signed, string anchor, DeviceGrantV2 grant, TrustChallenge challenge, long now)
    {
        var f = Verify<TrustFreshnessV2>(signed, anchor);
        Require(f.Version == 2 && f.VaultId == grant.VaultId && f.IssuerDeviceId == grant.IssuerDeviceId &&
            f.IssuerDeviceId == DeviceId(anchor) && f.TargetDeviceId == grant.RecipientDeviceId &&
            f.GrantId == grant.GrantId && f.TrustRevision == grant.Revision && !f.Revoked &&
            f.Nonce == challenge.Nonce && f.IssuedAt >= challenge.CreatedAt && f.IssuedAt <= now &&
            f.ExpiresAt > now && f.ExpiresAt - f.IssuedAt <= 60_000 && challenge.Consume(now));
    }
    public static DeviceResultV2 ValidateResult(string signed, string recipientKey, DeviceCommandV2 command, string commandEnvelope, long now)
    {
        var r = Verify<DeviceResultV2>(signed, recipientKey);
        Require(r.Version == 2 && r.IssuerDeviceId == DeviceId(recipientKey) && r.IssuerDeviceId == command.TargetDeviceId &&
            r.TargetDeviceId == command.IssuerDeviceId && r.VaultId == command.VaultId && r.CommandId == command.CommandId &&
            r.CommandDigest == Fingerprint(commandEnvelope) && r.GrantId == command.GrantId && r.TrustRevision == command.TrustRevision &&
            r.Result is "Completed" or "Failed" or "OutcomeUnknown" && r.IssuedAt >= command.IssuedAt && r.IssuedAt <= now);
        return r;
    }
}
