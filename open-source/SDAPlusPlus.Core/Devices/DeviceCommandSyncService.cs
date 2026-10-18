namespace SDAPlusPlus.Core.Devices;

public sealed record DeviceCommandSyncItem(DeviceExecutionReceipt Receipt, bool Acknowledged);
public sealed record DeviceCommandSyncResult(bool Success, IReadOnlyList<DeviceCommandSyncItem> Items, string? Detail = null);

/// <summary>Fail-closed compatibility boundary until verified E2E orchestration is integrated.</summary>
public sealed class DeviceCommandSyncService
{
    public DeviceCommandSyncService(IDeviceRegistryClient client, DeviceCommandProcessor processor)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(processor);
    }

    public Task<DeviceCommandSyncResult> SyncAsync(
        DeviceRegistrySettings settings, string localDeviceId,
        Func<bool> identityStillValid,
        Func<IncomingDeviceCommand, CancellationToken, Task<bool>> execute,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        // Local identity checks cannot replace an Admin signature and fresh trust proof.
        // Do not fetch, journal, execute, or ACK through the legacy entry point.
        return Task.FromResult(new DeviceCommandSyncResult(false, [],
            "Command execution is disabled pending verified E2E synchronization."));
    }
}
