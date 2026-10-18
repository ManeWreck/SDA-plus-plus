namespace SDAPlusPlus.Core.Devices;

public sealed record DeviceActionApproval(string VaultId, string RelayUrl, string DeviceId, string Action)
{
    public bool Allows(DeviceRegistrySettings settings, RegistryDevice? target) =>
        settings.IsConfigured && settings.Role == "Admin" && !settings.IsExpired &&
        settings.VaultId == VaultId && settings.RelayUrl == RelayUrl &&
        Action is "Lock" or "Revoke" or "WipeLocalData" &&
        !string.IsNullOrWhiteSpace(DeviceId) && target is not null &&
        target.DeviceId == DeviceId && target.Role != "Admin" && target.RevokedAt is null;
}
