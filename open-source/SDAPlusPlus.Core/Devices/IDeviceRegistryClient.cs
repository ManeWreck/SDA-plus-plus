namespace SDAPlusPlus.Core.Devices;

public interface IDeviceRegistryClient
{
    Task<DeviceListResult> ListDevicesAsync(DeviceRegistrySettings settings, CancellationToken cancellationToken = default);
    Task<DeviceAuditResult> ListAuditAsync(DeviceRegistrySettings settings, CancellationToken cancellationToken = default);
    Task<DeviceCommandResult> RevokeAsync(DeviceRegistrySettings settings, string deviceId, CancellationToken cancellationToken = default);
    Task<DeviceCommandResult> IssueCommandAsync(DeviceRegistrySettings settings, string deviceId, string commandType, CancellationToken cancellationToken = default);
    Task<DeviceInvitationResult> CreateInvitationAsync(
        DeviceRegistrySettings settings,
        string role,
        IReadOnlyList<DeviceInvitationAccount> accounts,
        IReadOnlyCollection<string> capabilities,
        CancellationToken cancellationToken = default);
    Task<DeviceClaimResult> ClaimInvitationAsync(
        string qrPayload,
        string verificationCode,
        string displayName,
        CancellationToken cancellationToken = default);
}
