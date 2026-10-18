using System.Security.Cryptography;

namespace SDAPlusPlus.Core.Devices;

public sealed record CommandSimulationReport(int FinalResults, int DeliveredResults, string? Detail = null);

// Test/experimental harness only. No production side effects or background scheduling are wired.
// Own the journal/outbox exclusively for this service's entire lifetime.
public sealed class VerifiedCommandSimulationService
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly DeviceRegistryClient client;
    private readonly VerifiedCommandReceiver receiver;
    private readonly IDeviceCommandJournal journal;
    private readonly DeviceResultOutbox outbox;
    private readonly IDeviceRequestSigner signer;
    private readonly TimeProvider clock;

    public VerifiedCommandSimulationService(HttpClient http, IDeviceRequestSigner signer,
        IDeviceCommandJournal journal, DeviceResultOutbox outbox, TimeProvider? clock = null)
    {
        this.signer = signer; this.journal = journal; this.outbox = outbox;
        this.clock = clock ?? TimeProvider.System;
        client = new(http, signer, resultOutbox: outbox);
        receiver = new(new(journal, this.clock), signer, this.clock);
    }

    public async Task<CommandSimulationReport> RunOnceAsync(Func<DeviceRegistrySettings> reloadTrust,
        Func<IncomingDeviceCommand, CancellationToken, Task<bool>> simulate, CancellationToken ct = default)
    {
        await gate.WaitAsync(ct);
        try
        {
            var trust = reloadTrust();
            string vault = trust.VaultId, anchor = trust.AdminPublicKey, grant = trust.E2EGrant, relay = trust.RelayUrl, role = trust.Role;
            void CheckTrust()
            {
                var current = reloadTrust();
                if (!current.IsConfigured || current.VaultId != vault || current.AdminPublicKey != anchor ||
                    current.E2EGrant != grant || current.RelayUrl != relay || current.Role != role ||
                    current.RevokedDevices.Contains(signer.DeviceId))
                    throw new CryptographicException("Local trust changed during synchronization.");
            }
            CheckTrust();
            // Exclusive serialized recovery: never race an active processor with Started reconciliation.
            await outbox.RecoverAsync(journal, vault, anchor, ct);
            CheckTrust();
            int delivered = await client.RetryAcknowledgmentsAsync(trust, ct);
            CheckTrust();
            if ((await journal.ReadRecoverableAsync(ct)).Any(r => r.State == DeviceExecutionState.OutcomeUnknown))
                return new(0, delivered, "Outcome reconciliation required; new actions are paused.");
            var pending = await client.PendingCommandsAsync(trust, ct);
            if (!pending.Success) return new(0, delivered, pending.Detail);
            int completed = 0;
            foreach (var command in pending.Commands)
            {
                CheckTrust(); ct.ThrowIfCancellationRequested();
                var old = await journal.ReadAsync(vault, signer.DeviceId, command.CommandId, ct);
                if (old is not null)
                {
                    if (old.Command != command) throw new CryptographicException("Conflicting replayed command.");
                    if (old.State == DeviceExecutionState.OutcomeUnknown)
                        return new(completed, delivered, "Outcome reconciliation required; no action retried.");
                    continue;
                }
                var challenge = new TrustChallenge(clock.GetUtcNow().ToUnixTimeMilliseconds());
                var published = await client.PublishFreshnessChallengeAsync(trust, challenge, ct);
                if (!published.Success) return new(completed, delivered, published.Detail);
                string? proof = null;
                for (int attempt = 0; attempt < 5; attempt++)
                {
                    CheckTrust();
                    proof = await client.FetchUnverifiedFreshnessProofAsync(trust, challenge, ct);
                    if (!string.IsNullOrEmpty(proof)) break;
                    if (attempt < 4) await Task.Delay(TimeSpan.FromSeconds(1), clock, ct);
                }
                CheckTrust();
                if (string.IsNullOrEmpty(proof)) return new(completed, delivered, "No fresh Admin proof; action not started.");
                var receipt = await receiver.VerifySimulateAndQueueAsync(trust, command.SignedEnvelope!, proof, challenge,
                    reloadTrust, simulate, outbox, ct);
                completed++;
                CheckTrust();
                var ack = await client.AcknowledgeCommandAsync(trust, receipt, ct);
                if (ack.Success) delivered++;
                if (receipt.State == DeviceExecutionState.OutcomeUnknown)
                    return new(completed, delivered, "Outcome reconciliation required; no action retried.");
            }
            return new(completed, delivered);
        }
        finally { gate.Release(); }
    }
}
