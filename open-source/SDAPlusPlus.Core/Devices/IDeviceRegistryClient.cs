namespace SDAPlusPlus.Core.Devices;

public interface IDeviceRegistryClient
{
    string RecipientCode => string.Empty;
    Task<DeviceInvitationResult> CreateBoundInvitationAsync(DeviceRegistrySettings settings, string recipientCode, string role,
        IReadOnlyList<DeviceInvitationAccount> accounts, IReadOnlyCollection<string> capabilities, CancellationToken cancellationToken = default)
        => Task.FromResult(new DeviceInvitationResult(false, Detail: "E2E enrollment unsupported."));
    Task<PendingDeviceCommandsResult> PendingCommandsAsync(DeviceRegistrySettings settings, CancellationToken cancellationToken = default)
        => Task.FromResult(new PendingDeviceCommandsResult(false, [], "Command polling is not supported by this client."));
    Task<DeviceCommandResult> AcknowledgeCommandAsync(DeviceRegistrySettings settings, DeviceExecutionReceipt receipt, CancellationToken cancellationToken = default)
        => Task.FromResult(new DeviceCommandResult(false, "Command acknowledgment is not supported by this client."));
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
