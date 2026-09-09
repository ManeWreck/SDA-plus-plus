using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;

namespace SDAPlusPlus.Core.Devices;

public sealed record DeviceInvitationAccount(string SteamId, byte[]? MaFilePayload = null);

public sealed record DeviceInvitationResult(
    bool Success,
    string? QrPayload = null,
    string? VerificationCode = null,
    long ExpiresAt = 0,
    string? Detail = null);

public sealed record GrantedAccountPayload(string SteamId, byte[] MaFilePayload);

public sealed record DeviceClaimResult(
    bool Success,
    DeviceRegistrySettings? Settings = null,
    IReadOnlyList<GrantedAccountPayload>? Accounts = null,
    string? Detail = null);

public sealed class RemoteActionAuthorization
{
    public string VaultId { get; init; } = string.Empty;
    public string InviteId { get; init; } = string.Empty;
    public string IssuerDeviceId { get; init; } = string.Empty;
    public string Role { get; init; } = string.Empty;
    public long IssuedAt { get; init; }
    public long? ExpiresAt { get; init; }
    public HashSet<string> AccountSteamIds { get; init; } = new(StringComparer.Ordinal);
    public HashSet<string> Capabilities { get; init; } = new(StringComparer.Ordinal);
}

public static class RemoteActionAuthorizationCodec
{
    private static readonly Regex IdPattern = new("^[A-Za-z0-9_-]{22}$", RegexOptions.CultureInvariant);
    private static readonly Regex SteamIdPattern = new("^[0-9]{17}$", RegexOptions.CultureInvariant);
    private static readonly HashSet<string> Roles = ["Admin", "TrustedOnline", "TrustedOffline", "Temporary"];
    private static readonly HashSet<string> Capabilities =
    [
        "ViewGuardCode", "ApproveSteamQr", "ViewConfirmations", "ApproveConfirmations", "TerminateSessions",
        "IssueTemporaryAccess", "SyncVault", "ExportVault", "ManageDevices"
    ];

    public static byte[] Canonical(RemoteActionAuthorization value)
    {
        if (!IdPattern.IsMatch(value.VaultId) || !IdPattern.IsMatch(value.InviteId) || !IdPattern.IsMatch(value.IssuerDeviceId))
            throw new ArgumentException("Invalid remote action authorization identity.");
        if (!Roles.Contains(value.Role) || value.IssuedAt <= 0 || value.ExpiresAt is long expires && expires <= value.IssuedAt)
            throw new ArgumentException("Invalid remote action authorization.");
        if (value.AccountSteamIds.Count > 64 || value.AccountSteamIds.Any(id => !SteamIdPattern.IsMatch(id)))
            throw new ArgumentException("Invalid remote action account grant.");
        if (value.Capabilities.Any(capability => !Capabilities.Contains(capability)))
            throw new ArgumentException("Invalid remote action capability.");

        string accountIds = string.Join(",", value.AccountSteamIds.OrderBy(id => id, StringComparer.Ordinal).Select(id => $"\"{id}\""));
        string capabilities = string.Join(",", value.Capabilities.OrderBy(item => item, StringComparer.Ordinal).Select(item => $"\"{item}\""));
        string json = "{\"version\":1" +
            $",\"vaultId\":\"{value.VaultId}\"" +
            $",\"inviteId\":\"{value.InviteId}\"" +
            $",\"issuerDeviceId\":\"{value.IssuerDeviceId}\"" +
            $",\"role\":\"{value.Role}\"" +
            $",\"issuedAt\":{value.IssuedAt}" +
            $",\"expiresAt\":{(value.ExpiresAt?.ToString() ?? "null")}" +
            $",\"accountSteamIds\":[{accountIds}]" +
            $",\"capabilities\":[{capabilities}]}}";
        return Encoding.UTF8.GetBytes(json);
    }

    public static string Fingerprint(byte[] canonical) => Base64Url(SHA256.HashData(canonical));

    public static RemoteActionAuthorization Parse(byte[] canonical)
    {
        if (canonical is not { Length: > 0 and <= 16384 })
            throw new ArgumentException("Invalid remote action authorization.");

        JObject root = JObject.Parse(Encoding.UTF8.GetString(canonical));
        if (root.Value<int>("version") != 1)
            throw new ArgumentException("Unsupported remote action authorization.");

        RemoteActionAuthorization value = new()
        {
            VaultId = root.Value<string>("vaultId") ?? string.Empty,
            InviteId = root.Value<string>("inviteId") ?? string.Empty,
            IssuerDeviceId = root.Value<string>("issuerDeviceId") ?? string.Empty,
            Role = root.Value<string>("role") ?? string.Empty,
            IssuedAt = root.Value<long>("issuedAt"),
            ExpiresAt = root.Value<long?>("expiresAt"),
            AccountSteamIds = root["accountSteamIds"]?.Values<string>().Where(item => item is not null).Select(item => item!).ToHashSet(StringComparer.Ordinal) ?? [],
            Capabilities = root["capabilities"]?.Values<string>().Where(item => item is not null).Select(item => item!).ToHashSet(StringComparer.Ordinal) ?? []
        };
        if (!Canonical(value).SequenceEqual(canonical))
            throw new ArgumentException("Remote action authorization is not canonical.");
        return value;
    }

    private static string Base64Url(byte[] value) =>
        Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
