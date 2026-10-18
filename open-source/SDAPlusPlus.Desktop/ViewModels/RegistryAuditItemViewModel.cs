using SDAPlusPlus.Core.Devices;

namespace SDAPlusPlus.Desktop.ViewModels;

public sealed class RegistryAuditItemViewModel(RegistryAuditEvent audit)
{
    public string Action => string.IsNullOrWhiteSpace(audit.Action) ? "unknown" : audit.Action;
    public string Occurred => audit.OccurredAt > 0
        ? DateTimeOffset.FromUnixTimeMilliseconds(audit.OccurredAt).ToLocalTime().ToString("g")
        : "—";
    public string Device => string.IsNullOrWhiteSpace(audit.TargetDeviceId)
        ? audit.ActorDeviceId
        : audit.TargetDeviceId;
    public string Country => string.IsNullOrWhiteSpace(audit.Country) ? "—" : audit.Country;
}
