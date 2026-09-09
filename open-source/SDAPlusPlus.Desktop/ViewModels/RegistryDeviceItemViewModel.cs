using SDAPlusPlus.Core.Devices;

namespace SDAPlusPlus.Desktop.ViewModels;

public sealed class RegistryDeviceItemViewModel
{
    private readonly bool _isRussian;

    public RegistryDeviceItemViewModel(RegistryDevice device, bool isRussian)
    {
        Device = device;
        _isRussian = isRussian;
    }

    public RegistryDevice Device { get; }
    public string Name => string.IsNullOrWhiteSpace(Device.DisplayName) ? Device.DeviceId : Device.DisplayName;
    public string DeviceId => Device.DeviceId;
    public string Role => Device.Role;
    public string LastSeen => Device.LastSeenAt > 0
        ? DateTimeOffset.FromUnixTimeMilliseconds(Device.LastSeenAt).ToLocalTime().ToString("g")
        : "—";
    public string Expires => Device.ExpiresAt is long expires
        ? DateTimeOffset.FromUnixTimeMilliseconds(expires).ToLocalTime().ToString("g")
        : "—";
    public string State => Device.RevokedAt is not null
        ? (_isRussian ? "Отозвано" : "Revoked")
        : Device.LockedAt is not null
            ? (_isRussian ? "Заблокировано" : "Locked")
            : (_isRussian ? "Активно" : "Active");
}
