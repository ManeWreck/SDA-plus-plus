namespace SDAPlusPlus.Core.Devices;

public enum DeviceExecutionState { Started, Completed, Failed, Expired, OutcomeUnknown }

public sealed record IncomingDeviceCommand(string VaultId, string DeviceId, string CommandId, string Type, long ExpiresAt);
public sealed record DeviceExecutionReceipt(IncomingDeviceCommand Command, DeviceExecutionState State);

public interface IDeviceCommandJournal
{
    Task<DeviceExecutionReceipt?> ReadAsync(string vaultId, string deviceId, string commandId, CancellationToken cancellationToken);
    // Must durably commit before returning; failures must throw, never return an empty journal.
    Task SaveAsync(DeviceExecutionReceipt receipt, CancellationToken cancellationToken);
}

/// <summary>
/// One instance per journal. The caller must authenticate commands before submitting them.
/// Execution ambiguity is retained rather than retrying destructive operations.
/// </summary>
public sealed class DeviceCommandProcessor(IDeviceCommandJournal journal, TimeProvider? timeProvider = null)
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;

    public async Task<DeviceExecutionReceipt> ProcessAsync(
        IncomingDeviceCommand command, string expectedVaultId, string expectedDeviceId,
        Func<IncomingDeviceCommand, CancellationToken, Task<bool>> execute,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(expectedVaultId) || string.IsNullOrWhiteSpace(expectedDeviceId) ||
            command.VaultId != expectedVaultId || command.DeviceId != expectedDeviceId ||
            !Guid.TryParseExact(command.CommandId, "D", out _) ||
            command.Type is not ("Lock" or "WipeLocalData" or "TerminateSteamSessions"))
            throw new ArgumentException("Invalid command identity, target or type.", nameof(command));

        await _gate.WaitAsync(cancellationToken);
        try
        {
            var existing = await journal.ReadAsync(command.VaultId, command.DeviceId, command.CommandId, cancellationToken);
            if (existing is not null)
            {
                if (existing.Command != command)
                    throw new InvalidOperationException("A command identifier was reused with different content.");
                if (existing.State != DeviceExecutionState.Started) return existing;
                var unknown = existing with { State = DeviceExecutionState.OutcomeUnknown };
                await journal.SaveAsync(unknown, CancellationToken.None);
                return unknown;
            }

            var receipt = new DeviceExecutionReceipt(command, command.ExpiresAt <= _time.GetUtcNow().ToUnixTimeMilliseconds()
                ? DeviceExecutionState.Expired : DeviceExecutionState.Started);
            await journal.SaveAsync(receipt, cancellationToken);
            if (receipt.State == DeviceExecutionState.Expired) return receipt;

            // Recheck after durable IO; cancellation/expiry here proves execution has not started.
            if (cancellationToken.IsCancellationRequested || command.ExpiresAt <= _time.GetUtcNow().ToUnixTimeMilliseconds())
            {
                receipt = receipt with { State = cancellationToken.IsCancellationRequested ? DeviceExecutionState.Failed : DeviceExecutionState.Expired };
            }
            else
            {
                try
                {
                    bool completed = await execute(command, cancellationToken);
                    receipt = receipt with { State = completed ? DeviceExecutionState.Completed : DeviceExecutionState.Failed };
                }
                catch (Exception)
                {
                    // Exceptions, including cancellation, do not prove that no side effect occurred.
                    receipt = receipt with { State = DeviceExecutionState.OutcomeUnknown };
                }
            }
            await journal.SaveAsync(receipt, CancellationToken.None);
            return receipt;
        }
        finally { _gate.Release(); }
    }
}
