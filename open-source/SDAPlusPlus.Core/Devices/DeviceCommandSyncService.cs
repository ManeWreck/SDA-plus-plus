namespace SDAPlusPlus.Core.Devices;

public sealed record DeviceCommandSyncItem(DeviceExecutionReceipt Receipt, bool Acknowledged);
public sealed record DeviceCommandSyncResult(bool Success, IReadOnlyList<DeviceCommandSyncItem> Items, string? Detail = null);

/// <summary>Caller owns the authenticated registry identity and a single persistent processor.</summary>
public sealed class DeviceCommandSyncService(IDeviceRegistryClient client, DeviceCommandProcessor processor)
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task<DeviceCommandSyncResult> SyncAsync(
        DeviceRegistrySettings settings, string localDeviceId,
        Func<bool> identityStillValid,
        Func<IncomingDeviceCommand, CancellationToken, Task<bool>> execute,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        List<DeviceCommandSyncItem> items = [];
        try
        {
            string vault = settings.VaultId;
            string relay = settings.RelayUrl;
            string role = settings.Role;
            bool Valid() => identityStillValid() && settings.IsConfigured && !settings.IsExpired &&
                settings.VaultId == vault && settings.RelayUrl == relay && settings.Role == role &&
                !string.IsNullOrWhiteSpace(localDeviceId);
            if (!Valid()) return new(false, items, "Device identity is unavailable or changed.");
            var pending = await client.PendingCommandsAsync(settings, cancellationToken);
            if (!pending.Success) return new(false, items, pending.Detail);
            // Validate the whole batch before executing any entry.
            if (pending.Commands.Any(command => command.VaultId != vault || command.DeviceId != localDeviceId) ||
                pending.Commands.Select(command => command.CommandId).Distinct(StringComparer.Ordinal).Count() != pending.Commands.Count)
                return new(false, items, "Command batch identity mismatch or duplicate identifier.");

            foreach (var command in pending.Commands)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!Valid()) return new(false, items, "Device identity changed during synchronization.");
                var receipt = await processor.ProcessAsync(command, vault, localDeviceId, (entry, token) =>
                {
                    // Recheck after journal IO and immediately before handing off side effects.
                    if (!Valid()) throw new InvalidOperationException("Device identity changed before execution.");
                    return execute(entry, token);
                }, cancellationToken);
                bool acknowledged = false;
                if (Valid() && receipt.State is DeviceExecutionState.Completed or DeviceExecutionState.Failed)
                {
                    var acknowledgment = await client.AcknowledgeCommandAsync(settings, receipt, cancellationToken);
                    acknowledged = acknowledgment.Success;
                }
                items.Add(new(receipt, acknowledged));
                // Keep ambiguous commands for reconciliation; never translate them into legacy Failed.
                if (receipt.State == DeviceExecutionState.OutcomeUnknown)
                    return new(false, items, "A command needs outcome reconciliation; execution has stopped.");
            }
            bool allAcknowledged = items.All(item => item.Acknowledged);
            return new(allAcknowledged, items, allAcknowledged ? null : "Some results remain unacknowledged; do not re-execute them.");
        }
        finally { _gate.Release(); }
    }
}
