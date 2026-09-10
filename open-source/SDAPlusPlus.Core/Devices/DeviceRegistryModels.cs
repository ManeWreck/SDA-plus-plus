namespace SDAPlusPlus.Core.Devices;

public sealed class DeviceRegistrySettings
{
    public string VaultId { get; set; } = string.Empty;
    public string RelayUrl { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public HashSet<string> GrantedAccountSteamIds { get; set; } = [];
    public HashSet<string> Capabilities { get; set; } = [];
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

    public bool IsConfigured => !string.IsNullOrWhiteSpace(VaultId) && !string.IsNullOrWhiteSpace(Role);
    public bool IsExpired => ExpiresAt is long value && value <= DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
}

public enum DeviceRegistryLoadState
{
    Missing,
    Ready,
    UnsupportedPlatform,
    Invalid
}

public sealed record DeviceRegistryLoadResult(
    DeviceRegistryLoadState State,
    DeviceRegistrySettings Settings,
    string? Detail = null);

public sealed record PairingRelayHealthResult(
    bool IsCompatible,
    string Endpoint,
    string? Storage,
    int ProtocolVersion,
    string? Detail = null);

public sealed class RegistryDevice
{
    [Newtonsoft.Json.JsonProperty("deviceId")]
    public string DeviceId { get; set; } = string.Empty;

    [Newtonsoft.Json.JsonProperty("displayName")]
    public string DisplayName { get; set; } = string.Empty;

    [Newtonsoft.Json.JsonProperty("role")]
    public string Role { get; set; } = string.Empty;

    [Newtonsoft.Json.JsonProperty("lastSeenAt")]
    public long LastSeenAt { get; set; }

    [Newtonsoft.Json.JsonProperty("expiresAt")]
    public long? ExpiresAt { get; set; }

    [Newtonsoft.Json.JsonProperty("revokedAt")]
    public long? RevokedAt { get; set; }

    [Newtonsoft.Json.JsonProperty("lockedAt")]
    public long? LockedAt { get; set; }

    [Newtonsoft.Json.JsonProperty("publicKey")]
    public string PublicKey { get; set; } = string.Empty;

    [Newtonsoft.Json.JsonProperty("encryptionPublicKey")]
    public string EncryptionPublicKey { get; set; } = string.Empty;

    [Newtonsoft.Json.JsonProperty("encryptionKeySignature")]
    public string EncryptionKeySignature { get; set; } = string.Empty;

    [Newtonsoft.Json.JsonProperty("grantFingerprint")]
    public string GrantFingerprint { get; set; } = string.Empty;
}

public sealed record DeviceListResult(bool Success, IReadOnlyList<RegistryDevice> Devices, string? Detail = null);

public sealed class RegistryAuditEvent
{
    [Newtonsoft.Json.JsonProperty("eventId")]
    public string EventId { get; set; } = string.Empty;

    [Newtonsoft.Json.JsonProperty("action")]
    public string Action { get; set; } = string.Empty;

    [Newtonsoft.Json.JsonProperty("actorDeviceId")]
    public string ActorDeviceId { get; set; } = string.Empty;

    [Newtonsoft.Json.JsonProperty("targetDeviceId")]
    public string TargetDeviceId { get; set; } = string.Empty;

    [Newtonsoft.Json.JsonProperty("occurredAt")]
    public long OccurredAt { get; set; }

    [Newtonsoft.Json.JsonProperty("country")]
    public string Country { get; set; } = string.Empty;
}

public sealed record DeviceAuditResult(bool Success, IReadOnlyList<RegistryAuditEvent> Events, string? Detail = null);

public sealed record DeviceCommandResult(bool Success, string? Detail = null);

public sealed record PendingDeviceCommandsResult(bool Success, IReadOnlyList<IncomingDeviceCommand> Commands, string? Detail = null);
