namespace SDAPlusPlus.Core.Devices;

/// <summary>Protocol verification harness. No application side effects are wired to this class.</summary>
public sealed class VerifiedCommandReceiver(DeviceCommandProcessor processor, IDeviceRequestSigner recipient, TimeProvider? clock = null)
{
    public const bool ProductionExecutionEnabled = false;
    private readonly TimeProvider time = clock ?? TimeProvider.System;
    public async Task<DeviceExecutionReceipt> VerifySimulateAndQueueAsync(DeviceRegistrySettings trust, string commandEnvelope,
        string freshnessEnvelope, TrustChallenge challenge, Func<DeviceRegistrySettings> reloadLocalTrust,
        Func<IncomingDeviceCommand, CancellationToken, Task<bool>> simulation, DeviceResultOutbox outbox, CancellationToken ct = default)
    {
        string adminKey = trust.AdminPublicKey;
        var receipt = await VerifyAndSimulateAsync(trust, commandEnvelope, freshnessEnvelope, challenge, reloadLocalTrust, simulation, ct);
        if (receipt.State is DeviceExecutionState.Completed or DeviceExecutionState.Failed or DeviceExecutionState.OutcomeUnknown)
            await outbox.EnqueueAsync(receipt, adminKey, CancellationToken.None);
        return receipt;
    }
    public async Task<DeviceExecutionReceipt> VerifyAndSimulateAsync(DeviceRegistrySettings trust, string commandEnvelope,
        string freshnessEnvelope, TrustChallenge challenge, Func<DeviceRegistrySettings> reloadLocalTrust,
        Func<IncomingDeviceCommand, CancellationToken, Task<bool>> simulation, CancellationToken ct = default)
    {
        long Now() => time.GetUtcNow().ToUnixTimeMilliseconds();
        string pinned = trust.AdminPublicKey, originalGrant = trust.E2EGrant;
        var parsed = EndToEndTrust.Verify<DeviceGrantV2>(originalGrant, pinned);
        var grant = EndToEndTrust.ValidateGrant(originalGrant, pinned, trust.VaultId, parsed.InviteId,
            new(recipient.DeviceId, recipient.PublicKey, recipient.EncryptionPublicKey, recipient.EncryptionKeySignature), Now());
        var command = EndToEndTrust.ValidateCommand(commandEnvelope, pinned, grant, Now());
        EndToEndTrust.ValidateFreshness(freshnessEnvelope, pinned, grant, challenge, Now());
        var proof = EndToEndTrust.Verify<TrustFreshnessV2>(freshnessEnvelope, pinned);
        var incoming = new IncomingDeviceCommand(command.VaultId, command.TargetDeviceId, command.CommandId, command.CommandType, command.ExpiresAt)
            { SignedEnvelope = commandEnvelope };
        return await processor.ProcessAsync(incoming, grant.VaultId, recipient.DeviceId, (entry, token) =>
        {
            var current = reloadLocalTrust();
            if (current.AdminPublicKey != pinned || current.E2EGrant != originalGrant || current.VaultId != grant.VaultId ||
                !current.IsConfigured || current.Role != grant.Role || current.RevokedDevices.Contains(recipient.DeviceId) ||
                current.ExpiresAt is long expiry && expiry <= Now() || Now() >= grant.ExpiresAt ||
                Now() >= proof.ExpiresAt || Now() < proof.IssuedAt)
                throw new System.Security.Cryptography.CryptographicException("Trust changed before simulation.");
            return simulation(entry, token);
        }, ct);
    }
}
