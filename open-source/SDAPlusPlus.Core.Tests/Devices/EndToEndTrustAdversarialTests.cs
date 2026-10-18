using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using SDAPlusPlus.Core.Devices;

namespace SDAPlusPlus.Core.Tests.Devices;

public sealed class EndToEndTrustAdversarialTests
{
    private const long Now = 1_800_000_000_000;

    [Theory]
    [InlineData("commandId", "00000000-0000-0000-0000-000000000001")]
    [InlineData("targetDeviceId", "attacker")]
    [InlineData("vaultId", "other-vault")]
    [InlineData("commandType", "WipeLocalData")]
    [InlineData("requiredCapability", "ViewGuardCode")]
    public void RelayCannotModifySignedCommand(string field, string replacement)
    {
        using var f = new Fixture();
        var envelope = JsonNode.Parse(EndToEndTrust.Sign(f.Command, f.Admin))!;
        string payload = Encoding.UTF8.GetString(EndToEndTrust.Decode(envelope["payload"]!.GetValue<string>()));
        int separator = payload.IndexOf('\n');
        var body = JsonNode.Parse(payload[(separator + 1)..])!;
        body[field] = replacement;
        envelope["payload"] = EndToEndTrust.Encode(Encoding.UTF8.GetBytes(payload[..(separator + 1)] + body.ToJsonString()));
        Assert.Throws<CryptographicException>(() => EndToEndTrust.ValidateCommand(envelope.ToJsonString(), f.Admin.PublicKey, f.Grant, Now));
    }

    [Fact]
    public void EvenAdminSignedCommandsMustMatchLocalAuthorizationContext()
    {
        using var f = new Fixture();
        DeviceCommandV2[] invalid =
        [
            f.Command with { Version = 1 },
            f.Command with { VaultId = "other" },
            f.Command with { TargetDeviceId = f.Admin.DeviceId },
            f.Command with { IssuerDeviceId = f.Recipient.DeviceId },
            f.Command with { RequiredCapability = "ViewGuardCode" },
            f.Command with { TrustRevision = 2 },
            f.Command with { GrantId = Guid.NewGuid().ToString("D") },
            f.Command with { ExpiresAt = Now },
            f.Command with { CommandType = "Unknown" }
        ];
        foreach (var command in invalid)
            Assert.Throws<CryptographicException>(() => EndToEndTrust.ValidateCommand(
                EndToEndTrust.Sign(command, f.Admin), f.Admin.PublicKey, f.Grant, Now));
        Assert.Equal(f.Command, EndToEndTrust.ValidateCommand(EndToEndTrust.Sign(f.Command, f.Admin), f.Admin.PublicKey, f.Grant, Now));
    }

    [Fact]
    public void RelayCannotSubstituteAdminOrDecryptRecipientGrant()
    {
        using var f = new Fixture();
        using var attacker = new Signer();
        string signed = EndToEndTrust.Sign(f.Grant, f.Admin);
        string encrypted = EndToEndTrust.EncryptGrant(signed, f.Recipient.EncryptionPublicKey, "vault", "invite");
        Assert.Equal(signed, EndToEndTrust.DecryptGrant(encrypted, f.Recipient, "vault", "invite"));
        Assert.Throws<CryptographicException>(() => EndToEndTrust.DecryptGrant(encrypted, attacker, "vault", "invite"));
        Assert.Throws<AuthenticationTagMismatchException>(() => EndToEndTrust.DecryptGrant(encrypted, f.Recipient, "other", "invite"));
        var forged = f.Grant with { IssuerDeviceId = attacker.DeviceId };
        Assert.Throws<CryptographicException>(() => EndToEndTrust.ValidateGrant(
            EndToEndTrust.Sign(forged, attacker), f.Admin.PublicKey, "vault", "invite", f.Identity, Now));
        Assert.Throws<CryptographicException>(() => EndToEndTrust.ValidateGrant(
            EndToEndTrust.Sign(f.Grant with { Role = "Admin" }, f.Admin), f.Admin.PublicKey, "vault", "invite", f.Identity, Now));
        Assert.Equal(f.Grant.GrantId, EndToEndTrust.ValidateGrant(signed, f.Admin.PublicKey, "vault", "invite", f.Identity, Now).GrantId);
    }

    [Fact]
    public void FreshnessRejectsRevocationWrongNonceStaleRevisionAndReplay()
    {
        using var f = new Fixture();
        var challenge = new TrustChallenge(Now);
        var proof = new TrustFreshnessV2(2, "vault", f.Admin.DeviceId, f.Recipient.DeviceId,
            f.Grant.GrantId, 1, challenge.Nonce, Now, Now + 30_000, false);
        TrustFreshnessV2[] invalid =
        [
            proof with { Revoked = true }, proof with { Nonce = "wrong" },
            proof with { TrustRevision = 2 }, proof with { ExpiresAt = Now },
            proof with { TargetDeviceId = f.Admin.DeviceId }
        ];
        foreach (var value in invalid)
            Assert.Throws<CryptographicException>(() => EndToEndTrust.ValidateFreshness(
                EndToEndTrust.Sign(value, f.Admin), f.Admin.PublicKey, f.Grant, challenge, Now));
        string signed = EndToEndTrust.Sign(proof, f.Admin);
        EndToEndTrust.ValidateFreshness(signed, f.Admin.PublicKey, f.Grant, challenge, Now);
        Assert.Throws<CryptographicException>(() => EndToEndTrust.ValidateFreshness(signed, f.Admin.PublicKey, f.Grant, challenge, Now));
        Assert.Throws<CryptographicException>(() => EndToEndTrust.ValidateFreshness(signed, f.Admin.PublicKey, f.Grant, new TrustChallenge(Now), Now));
    }

    [Fact]
    public void ResultMustBeRecipientSignedAndBoundToExactCommand()
    {
        using var f = new Fixture();
        string command = EndToEndTrust.Sign(f.Command, f.Admin);
        var result = new DeviceResultV2(2, "vault", f.Command.CommandId, EndToEndTrust.Fingerprint(command),
            f.Recipient.DeviceId, f.Admin.DeviceId, "OutcomeUnknown", Now, f.Grant.GrantId, 1);
        string signed = EndToEndTrust.Sign(result, f.Recipient);
        Assert.Equal(result, EndToEndTrust.ValidateResult(signed, f.Recipient.PublicKey, f.Command, command, Now));
        Assert.Throws<CryptographicException>(() => EndToEndTrust.ValidateResult(
            EndToEndTrust.Sign(result, f.Admin), f.Recipient.PublicKey, f.Command, command, Now));
        Assert.Throws<CryptographicException>(() => EndToEndTrust.ValidateResult(signed, f.Recipient.PublicKey,
            f.Command with { CommandId = Guid.NewGuid().ToString("D") }, command, Now));
        Assert.Throws<CryptographicException>(() => EndToEndTrust.ValidateResult(signed, f.Recipient.PublicKey, f.Command, command + " ", Now));
        Assert.Throws<CryptographicException>(() => EndToEndTrust.ValidateResult(
            EndToEndTrust.Sign(result with { Result = "Pending" }, f.Recipient), f.Recipient.PublicKey, f.Command, command, Now));
    }

    [Fact]
    public async Task VerifiedRedeliveryAfterRestartDoesNotRepeatSimulation()
    {
        using var f = new Fixture();
        var journal = new MemoryJournal();
        var clock = new FixedClock();
        var trust = Trust(f);
        string command = EndToEndTrust.Sign(f.Command, f.Admin);
        int actions = 0;
        for (int attempt = 0; attempt < 2; attempt++)
        {
            var challenge = new TrustChallenge(Now);
            var receiver = new VerifiedCommandReceiver(new(journal, clock), f.Recipient, clock);
            var receipt = await receiver.VerifyAndSimulateAsync(trust, command, Proof(f, challenge), challenge,
                () => trust, (_, _) => { actions++; return Task.FromResult(true); });
            Assert.Equal(DeviceExecutionState.Completed, receipt.State);
        }
        Assert.Equal(1, actions);
        Assert.Equal(2, journal.Saves);
    }

    [Theory]
    [InlineData("revoked")]
    [InlineData("role")]
    [InlineData("grant")]
    [InlineData("expired")]
    public async Task TrustChangeAfterCheckpointPreventsSimulation(string change)
    {
        using var f = new Fixture();
        var trust = Trust(f);
        var journal = new MemoryJournal { AfterStarted = () =>
        {
            if (change == "revoked") trust.RevokedDevices.Add(f.Recipient.DeviceId);
            if (change == "role") trust.Role = "Admin";
            if (change == "grant") trust.E2EGrant = "changed";
            if (change == "expired") trust.ExpiresAt = Now;
        }};
        var clock = new FixedClock();
        var receiver = new VerifiedCommandReceiver(new(journal, clock), f.Recipient, clock);
        var challenge = new TrustChallenge(Now);
        int actions = 0;
        var receipt = await receiver.VerifyAndSimulateAsync(trust, EndToEndTrust.Sign(f.Command, f.Admin),
            Proof(f, challenge), challenge, () => trust,
            (_, _) => { actions++; return Task.FromResult(true); });
        Assert.Equal(0, actions);
        Assert.Equal(DeviceExecutionState.OutcomeUnknown, receipt.State);
    }

    [Theory]
    [InlineData(DeviceExecutionState.Completed)]
    [InlineData(DeviceExecutionState.Failed)]
    [InlineData(DeviceExecutionState.OutcomeUnknown)]
    public void FinalReceiptRemainsReportableAfterCommandExpiry(DeviceExecutionState state)
    {
        using var f = new Fixture();
        string envelope = EndToEndTrust.Sign(f.Command, f.Admin);
        var entry = new IncomingDeviceCommand("vault", f.Recipient.DeviceId, f.Command.CommandId, "Lock", f.Command.ExpiresAt)
            { SignedEnvelope = envelope };
        long later = f.Command.ExpiresAt + 1000;
        string signed = SignedDeviceReceipt.Create(new(entry, state), "vault", f.Admin.PublicKey, f.Recipient, later);
        var verified = EndToEndTrust.ValidateResult(signed, f.Recipient.PublicKey, f.Command, envelope, later);
        Assert.Equal(state.ToString(), verified.Result);
        Assert.Throws<CryptographicException>(() => SignedDeviceReceipt.Create(
            new(entry with { ExpiresAt = entry.ExpiresAt + 1 }, state), "vault", f.Admin.PublicKey, f.Recipient, later));
    }

    [Theory]
    [InlineData(DeviceExecutionState.Started)]
    [InlineData(DeviceExecutionState.Expired)]
    public void NonFinalReceiptCannotBecomeSignedExecutionResult(DeviceExecutionState state)
    {
        using var f = new Fixture();
        var entry = new IncomingDeviceCommand("vault", f.Recipient.DeviceId, f.Command.CommandId, "Lock", f.Command.ExpiresAt)
            { SignedEnvelope = EndToEndTrust.Sign(f.Command, f.Admin) };
        Assert.Throws<CryptographicException>(() => SignedDeviceReceipt.Create(new(entry, state),
            "vault", f.Admin.PublicKey, f.Recipient, Now));
    }

    [Theory]
    [InlineData(DeviceExecutionState.Completed)]
    [InlineData(DeviceExecutionState.OutcomeUnknown)]
    public async Task OutboxRetriesIdenticalSignedBytesAfterRestartAndExpiry(DeviceExecutionState state)
    {
        if (!OperatingSystem.IsWindows()) return;
        using var f = new Fixture();
        string directory = Path.Combine(Path.GetTempPath(), "sdapp-outbox-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            string envelope = EndToEndTrust.Sign(f.Command, f.Admin);
            var receipt = new DeviceExecutionReceipt(new("vault", f.Recipient.DeviceId, f.Command.CommandId,
                "Lock", f.Command.ExpiresAt) { SignedEnvelope = envelope }, state);
            var first = new DeviceResultOutbox(directory, f.Recipient, new FixedClock());
            var queued = await first.EnqueueAsync(receipt, f.Admin.PublicKey);
            int sends = 0;
            Assert.Equal(0, await first.RetryAsync("vault", f.Admin.PublicKey, (item, _) =>
            {
                Assert.Equal(queued.SignedResult, item.SignedResult);
                sends++;
                return Task.FromResult(false);
            }));
            var restarted = new DeviceResultOutbox(directory, f.Recipient, new LaterClock());
            Assert.Equal(queued, await restarted.EnqueueAsync(receipt, f.Admin.PublicKey));
            for (int retry = 0; retry < 2; retry++)
                Assert.Equal(1, await restarted.RetryAsync("vault", f.Admin.PublicKey, (item, _) =>
                {
                    Assert.Equal(queued.SignedResult, item.SignedResult);
                    var result = AdminCommandResultVerifier.Verify(new(envelope, f.Recipient.PublicKey), item.SignedResult,
                        f.Admin.PublicKey, "vault", Now + 200_000);
                    Assert.Equal(state.ToString(), result.Result);
                    sends++;
                    return Task.FromResult(true);
                }));
            Assert.Equal(3, sends);
            Assert.Single(Directory.GetFiles(directory, "*.ack"));
            await Assert.ThrowsAsync<InvalidDataException>(() => restarted.EnqueueAsync(
                receipt with { State = DeviceExecutionState.Failed }, f.Admin.PublicKey));
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task DamagedOutboxCannotSendOrSilentlyReplaceResult()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var f = new Fixture();
        string directory = Path.Combine(Path.GetTempPath(), "sdapp-outbox-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            var receipt = new DeviceExecutionReceipt(new("vault", f.Recipient.DeviceId, f.Command.CommandId,
                "Lock", f.Command.ExpiresAt) { SignedEnvelope = EndToEndTrust.Sign(f.Command, f.Admin) }, DeviceExecutionState.Completed);
            var outbox = new DeviceResultOutbox(directory, f.Recipient, new FixedClock());
            await outbox.EnqueueAsync(receipt, f.Admin.PublicKey);
            string path = Assert.Single(Directory.GetFiles(directory, "*.ack"));
            File.WriteAllBytes(path, [1, 2, 3]);
            int sends = 0;
            await Assert.ThrowsAnyAsync<CryptographicException>(() => outbox.RetryAsync("vault", f.Admin.PublicKey,
                (_, _) => { sends++; return Task.FromResult(true); }));
            await Assert.ThrowsAnyAsync<CryptographicException>(() => outbox.EnqueueAsync(receipt, f.Admin.PublicKey));
            Assert.Equal(0, sends);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [Fact]
    public void AdminRejectsResultUsingRelaySubstitutedRecipientKey()
    {
        using var f = new Fixture();
        using var attacker = new Signer();
        string envelope = EndToEndTrust.Sign(f.Command, f.Admin);
        var result = new DeviceResultV2(2, "vault", f.Command.CommandId, EndToEndTrust.Fingerprint(envelope),
            attacker.DeviceId, f.Admin.DeviceId, "Completed", Now, f.Grant.GrantId, 1);
        Assert.Throws<CryptographicException>(() => AdminCommandResultVerifier.Verify(new(envelope, f.Recipient.PublicKey),
            EndToEndTrust.Sign(result, attacker), f.Admin.PublicKey, "vault", Now));
        Assert.Throws<CryptographicException>(() => AdminCommandResultVerifier.Verify(new(envelope, attacker.PublicKey),
            EndToEndTrust.Sign(result, attacker), f.Admin.PublicKey, "vault", Now));
    }

    [Theory]
    [InlineData(DeviceExecutionState.Completed, DeviceExecutionState.Completed)]
    [InlineData(DeviceExecutionState.Started, DeviceExecutionState.OutcomeUnknown)]
    public async Task CrashBeforeEnqueueRecoversFromDurableJournalWithoutExecution(DeviceExecutionState initial, DeviceExecutionState expected)
    {
        if (!OperatingSystem.IsWindows()) return;
        using var f = new Fixture();
        string directory = Path.Combine(Path.GetTempPath(), "sdapp-recovery-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            string journalPath = Path.Combine(directory, "journal"), queuePath = Path.Combine(directory, "queue");
            var command = new IncomingDeviceCommand("vault", f.Recipient.DeviceId, f.Command.CommandId, "Lock", f.Command.ExpiresAt)
                { SignedEnvelope = EndToEndTrust.Sign(f.Command, f.Admin) };
            await new WindowsDeviceCommandJournal(journalPath).SaveAsync(new(command, initial), CancellationToken.None);
            var restartedJournal = new WindowsDeviceCommandJournal(journalPath);
            var outbox = new DeviceResultOutbox(queuePath, f.Recipient, new LaterClock());
            await outbox.RecoverAsync(restartedJournal, "vault", f.Admin.PublicKey);
            Assert.Equal(expected, (await restartedJournal.ReadAsync("vault", f.Recipient.DeviceId, f.Command.CommandId, CancellationToken.None))!.State);
            Assert.Equal(1, await outbox.RetryAsync("vault", f.Admin.PublicKey, (item, _) =>
            {
                Assert.Equal(expected, item.Receipt.State);
                return Task.FromResult(true);
            }));
            await outbox.RecoverAsync(restartedJournal, "vault", f.Admin.PublicKey);
            Assert.Single(Directory.GetFiles(queuePath, "*.ack"));
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task VerifiedSimulationQueuesSignedReceiptEndToEnd()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var f = new Fixture();
        string directory = Path.Combine(Path.GetTempPath(), "sdapp-simulation-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            var clock = new FixedClock();
            var trust = Trust(f);
            var receiver = new VerifiedCommandReceiver(new(new MemoryJournal(), clock), f.Recipient, clock);
            var outbox = new DeviceResultOutbox(directory, f.Recipient, clock);
            var challenge = new TrustChallenge(Now);
            string envelope = EndToEndTrust.Sign(f.Command, f.Admin);
            int actions = 0;
            var receipt = await receiver.VerifySimulateAndQueueAsync(trust, envelope, Proof(f, challenge), challenge, () => trust,
                (_, _) => { actions++; return Task.FromResult(true); }, outbox);
            Assert.Equal(DeviceExecutionState.Completed, receipt.State);
            Assert.Equal(1, await outbox.RetryAsync("vault", f.Admin.PublicKey, (item, _) =>
            {
                Assert.Equal("Completed", AdminCommandResultVerifier.Verify(new(envelope, f.Recipient.PublicKey),
                    item.SignedResult, f.Admin.PublicKey, "vault", Now).Result);
                return Task.FromResult(true);
            }));
            Assert.Equal(1, actions);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task RegistryTransportRetriesStoredAckAfterHttpFailure()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var f = new Fixture();
        string directory = Path.Combine(Path.GetTempPath(), "sdapp-http-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            var handler = new AckHandler();
            using var http = new HttpClient(handler);
            var trust = Trust(f);
            trust.RelayUrl = "https://relay.example";
            string envelope = EndToEndTrust.Sign(f.Command, f.Admin);
            var receipt = new DeviceExecutionReceipt(new("vault", f.Recipient.DeviceId, f.Command.CommandId,
                "Lock", f.Command.ExpiresAt) { SignedEnvelope = envelope }, DeviceExecutionState.OutcomeUnknown);
            var client = new DeviceRegistryClient(http, f.Recipient,
                resultOutbox: new DeviceResultOutbox(directory, f.Recipient, new FixedClock()));
            Assert.False((await client.AcknowledgeCommandAsync(trust, receipt)).Success);
            var restarted = new DeviceRegistryClient(http, f.Recipient,
                resultOutbox: new DeviceResultOutbox(directory, f.Recipient, new LaterClock()));
            Assert.Equal(1, await restarted.RetryAcknowledgmentsAsync(trust));
            Assert.Equal(2, handler.Bodies.Count);
            Assert.Equal(handler.Bodies[0], handler.Bodies[1]);
            var body = JsonNode.Parse(handler.Bodies[1])!;
            Assert.Equal("OutcomeUnknown", body["status"]!.GetValue<string>());
            Assert.Equal("OutcomeUnknown", AdminCommandResultVerifier.Verify(new(envelope, f.Recipient.PublicKey),
                body["signedResult"]!.GetValue<string>(), f.Admin.PublicKey, "vault", Now + 200_000).Result);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
    private sealed class AckHandler : HttpMessageHandler
    {
        public List<string> Bodies { get; } = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.EndsWith("/ack", request.RequestUri!.AbsolutePath);
            Bodies.Add(await request.Content!.ReadAsStringAsync(ct));
            return new HttpResponseMessage(Bodies.Count == 1 ? System.Net.HttpStatusCode.ServiceUnavailable : System.Net.HttpStatusCode.OK)
                { Content = new StringContent("{}") };
        }
    }

    [Theory]
    [InlineData("Completed", "Failed")]
    [InlineData("OutcomeUnknown", "Completed")]
    public async Task AdminInboxPreservesFirstResultAndConflictAcrossRestart(string firstState, string otherState)
    {
        if (!OperatingSystem.IsWindows()) return;
        using var f = new Fixture();
        string directory = Path.Combine(Path.GetTempPath(), "sdapp-inbox-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            string envelope = EndToEndTrust.Sign(f.Command, f.Admin);
            var issued = new IssuedDeviceCommand(envelope, f.Recipient.PublicKey);
            string Result(string state, long at) => EndToEndTrust.Sign(new DeviceResultV2(2, "vault", f.Command.CommandId,
                EndToEndTrust.Fingerprint(envelope), f.Recipient.DeviceId, f.Admin.DeviceId, state, at, f.Grant.GrantId, 1), f.Recipient);
            string first = Result(firstState, Now);
            var inbox = new AdminResultInbox(directory, new LaterClock());
            var accepted = await inbox.AcceptAsync(issued, first, f.Admin.PublicKey, "vault");
            Assert.False(accepted.Duplicate);
            Assert.Equal(firstState == "OutcomeUnknown", accepted.RequiresReconciliation);
            string path = Assert.Single(Directory.GetFiles(directory, "*.result"));
            byte[] before = File.ReadAllBytes(path);
            var duplicate = await new AdminResultInbox(directory, new LaterClock()).AcceptAsync(
                issued, Result(firstState, Now + 1), f.Admin.PublicKey, "vault");
            Assert.True(duplicate.Duplicate);
            Assert.Equal(before, File.ReadAllBytes(path));
            var conflicting = await inbox.AcceptAsync(issued, Result(otherState, Now + 2), f.Admin.PublicKey, "vault");
            Assert.True(conflicting.RequiresReconciliation);
            Assert.Equal(firstState, conflicting.FirstResult.Result);
            var restored = await new AdminResultInbox(directory, new LaterClock()).AcceptAsync(issued, first, f.Admin.PublicKey, "vault");
            Assert.True(restored.Duplicate);
            Assert.True(restored.RequiresReconciliation);
            Assert.Equal(firstState, restored.FirstResult.Result);
            var offline = await new AdminResultInbox(directory, new LaterClock()).ReadAsync(issued, f.Admin.PublicKey, "vault");
            Assert.NotNull(offline);
            Assert.True(offline.RequiresReconciliation);
            Assert.Equal(firstState, offline.FirstResult.Result);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task AdminInboxRejectsForgedResultWithoutWritingAndRejectsCorruption()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var f = new Fixture();
        using var attacker = new Signer();
        string directory = Path.Combine(Path.GetTempPath(), "sdapp-inbox-invalid-" + Guid.NewGuid().ToString("N"));
        try
        {
            string envelope = EndToEndTrust.Sign(f.Command, f.Admin);
            var issued = new IssuedDeviceCommand(envelope, f.Recipient.PublicKey);
            var result = new DeviceResultV2(2, "vault", f.Command.CommandId, EndToEndTrust.Fingerprint(envelope),
                f.Recipient.DeviceId, f.Admin.DeviceId, "Completed", Now, f.Grant.GrantId, 1);
            var inbox = new AdminResultInbox(directory, new FixedClock());
            await Assert.ThrowsAnyAsync<CryptographicException>(() => inbox.AcceptAsync(issued,
                EndToEndTrust.Sign(result, attacker), f.Admin.PublicKey, "vault"));
            Assert.False(Directory.Exists(directory));
            string valid = EndToEndTrust.Sign(result, f.Recipient);
            await inbox.AcceptAsync(issued, valid, f.Admin.PublicKey, "vault");
            string path = Assert.Single(Directory.GetFiles(directory, "*.result"));
            File.WriteAllBytes(path, [7, 8, 9]);
            await Assert.ThrowsAnyAsync<CryptographicException>(() => inbox.AcceptAsync(issued, valid, f.Admin.PublicKey, "vault"));
            Assert.Equal(new byte[] { 7, 8, 9 }, File.ReadAllBytes(path));
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task AdminInboxCannotReportAcceptanceWhenStorageFails()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var f = new Fixture();
        string path = Path.Combine(Path.GetTempPath(), "sdapp-inbox-blocked-" + Guid.NewGuid().ToString("N"));
        try
        {
            File.WriteAllText(path, "synthetic blocker");
            string envelope = EndToEndTrust.Sign(f.Command, f.Admin);
            var result = new DeviceResultV2(2, "vault", f.Command.CommandId, EndToEndTrust.Fingerprint(envelope),
                f.Recipient.DeviceId, f.Admin.DeviceId, "Completed", Now, f.Grant.GrantId, 1);
            await Assert.ThrowsAnyAsync<IOException>(() => new AdminResultInbox(path, new FixedClock()).AcceptAsync(
                new(envelope, f.Recipient.PublicKey), EndToEndTrust.Sign(result, f.Recipient), f.Admin.PublicKey, "vault"));
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FreshnessClientsExchangeProofButLocalRevocationStillWins(bool revoked)
    {
        using var f = new Fixture();
        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var grant = f.Grant with { IssuedAt = now - 1000, ExpiresAt = now + 120_000 };
        string signedGrant = EndToEndTrust.Sign(grant, f.Admin);
        var recipientSettings = new DeviceRegistrySettings { VaultId = "vault", Role = grant.Role,
            RelayUrl = "https://relay.example", AdminPublicKey = f.Admin.PublicKey, E2EGrant = signedGrant };
        var adminSettings = new DeviceRegistrySettings { VaultId = "vault", Role = "Admin", RelayUrl = "https://relay.example" };
        adminSettings.IssuedGrants.Add(f.Recipient.DeviceId, signedGrant);
        if (revoked) adminSettings.RevokedDevices.Add(f.Recipient.DeviceId);
        var handler = new FreshnessHandler(f.Recipient.DeviceId);
        using var http = new HttpClient(handler);
        var recipient = new DeviceRegistryClient(http, f.Recipient);
        var admin = new DeviceRegistryClient(http, f.Admin);
        var challenge = new TrustChallenge(now);
        Assert.True((await recipient.PublishFreshnessChallengeAsync(recipientSettings, challenge)).Success);
        Assert.Null(await recipient.FetchUnverifiedFreshnessProofAsync(recipientSettings, challenge));
        Assert.Equal(1, await admin.RespondToFreshnessChallengesAsync(adminSettings));
        string proof = (await recipient.FetchUnverifiedFreshnessProofAsync(recipientSettings, challenge))!;
        Assert.NotNull(proof);
        if (revoked)
            Assert.Throws<CryptographicException>(() => EndToEndTrust.ValidateFreshness(proof, f.Admin.PublicKey, grant,
                challenge, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()));
        else
            EndToEndTrust.ValidateFreshness(proof, f.Admin.PublicKey, grant, challenge, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        Assert.Equal(challenge.Nonce, handler.Nonce);
    }

    private sealed class FreshnessHandler(string deviceId) : HttpMessageHandler
    {
        public string? Nonce { get; private set; }
        private string? proof;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            JsonObject response;
            if (request.Method == HttpMethod.Post)
            {
                Nonce = JsonNode.Parse(await request.Content!.ReadAsStringAsync(ct))!["nonce"]!.GetValue<string>();
                response = new();
            }
            else if (request.Method == HttpMethod.Put)
            {
                Assert.EndsWith($"/{deviceId}/{Nonce}", request.RequestUri!.AbsolutePath);
                proof = JsonNode.Parse(await request.Content!.ReadAsStringAsync(ct))!["signedProof"]!.GetValue<string>();
                response = new();
            }
            else if (request.RequestUri!.AbsolutePath.EndsWith("/freshness"))
                response = new() { ["challenges"] = new JsonArray(new JsonObject { ["deviceId"] = deviceId, ["nonce"] = Nonce }) };
            else response = new() { ["signedProof"] = proof };
            return new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new StringContent(response.ToJsonString()) };
        }
    }

    [Theory]
    [InlineData("lost-ack")]
    [InlineData("unknown")]
    [InlineData("forged-proof")]
    [InlineData("missing-proof")]
    [InlineData("changed-trust")]
    public async Task CompleteSimulationCycleFailsClosedAndNeverRepeatsAction(string mode)
    {
        if (!OperatingSystem.IsWindows()) return;
        using var f = new Fixture();
        string directory = Path.Combine(Path.GetTempPath(), "sdapp-cycle-" + Guid.NewGuid().ToString("N"));
        try
        {
            long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var grant = f.Grant with { IssuedAt = now - 1000, ExpiresAt = now + 120_000 };
            var command = f.Command with { IssuedAt = now, ExpiresAt = now + 60_000 };
            var trust = new DeviceRegistrySettings { VaultId = "vault", Role = grant.Role, RelayUrl = "https://relay.example",
                AdminPublicKey = f.Admin.PublicKey, E2EGrant = EndToEndTrust.Sign(grant, f.Admin) };
            var handler = new CycleHandler(f.Admin, grant, command, mode, () => trust.Role = "Admin");
            using var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(10) };
            VerifiedCommandSimulationService Service() => new(http, f.Recipient,
                new WindowsDeviceCommandJournal(Path.Combine(directory, "journal")),
                new DeviceResultOutbox(Path.Combine(directory, "outbox"), f.Recipient));
            int actions = 0;
            Task<bool> Simulate(IncomingDeviceCommand _, CancellationToken token)
            {
                actions++;
                if (mode == "unknown") throw new IOException("Synthetic ambiguous result");
                return Task.FromResult(true);
            }
            if (mode is "forged-proof" or "changed-trust")
                await Assert.ThrowsAnyAsync<CryptographicException>(() => Service().RunOnceAsync(() => trust, Simulate));
            else
            {
                var report = await Service().RunOnceAsync(() => trust, Simulate);
                if (mode == "missing-proof")
                {
                    Assert.Contains("No fresh Admin proof", report.Detail);
                    Assert.Equal(5, handler.ProofPolls);
                }
                else
                {
                    Assert.Equal(1, actions);
                    var restarted = await Service().RunOnceAsync(() => trust, Simulate);
                    Assert.Equal(0, restarted.FinalResults);
                    Assert.Equal(1, actions);
                    Assert.Equal(2, handler.Acks.Count);
                    Assert.Equal(handler.Acks[0], handler.Acks[1]);
                    if (mode == "unknown") Assert.Contains("reconciliation", restarted.Detail);
                }
            }
            if (mode is "forged-proof" or "changed-trust" or "missing-proof")
            {
                Assert.Equal(0, actions);
                Assert.Empty(handler.Acks);
                Assert.False(Directory.Exists(Path.Combine(directory, "journal")));
            }
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    private sealed class CycleHandler(Signer admin, DeviceGrantV2 grant, DeviceCommandV2 command, string mode, Action changeTrust) : HttpMessageHandler
    {
        private readonly string envelope = EndToEndTrust.Sign(command, admin);
        private string? proof;
        public List<string> Acks { get; } = [];
        public int ProofPolls { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            string path = request.RequestUri!.AbsolutePath;
            JsonObject body;
            var status = System.Net.HttpStatusCode.OK;
            if (path.EndsWith("/commands"))
                body = new() { ["commands"] = new JsonArray(new JsonObject { ["commandId"] = command.CommandId,
                    ["targetDeviceId"] = command.TargetDeviceId, ["type"] = command.CommandType,
                    ["expiresAt"] = command.ExpiresAt, ["signedEnvelope"] = envelope }) };
            else if (path.EndsWith("/ack"))
            {
                Acks.Add(await request.Content!.ReadAsStringAsync(ct));
                if (Acks.Count == 1) status = System.Net.HttpStatusCode.ServiceUnavailable;
                body = new();
            }
            else if (request.Method == HttpMethod.Post)
            {
                string nonce = JsonNode.Parse(await request.Content!.ReadAsStringAsync(ct))!["nonce"]!.GetValue<string>();
                long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                proof = EndToEndTrust.Sign(new TrustFreshnessV2(2, grant.VaultId, grant.IssuerDeviceId, grant.RecipientDeviceId,
                    grant.GrantId, grant.Revision, mode == "forged-proof" ? "wrong-nonce" : nonce, now, now + 30_000, false), admin);
                body = new();
            }
            else
            {
                ProofPolls++;
                if (mode == "changed-trust") changeTrust();
                body = new() { ["signedProof"] = mode == "missing-proof" ? null : proof };
            }
            return new HttpResponseMessage(status) { Content = new StringContent(body.ToJsonString()) };
        }
    }

    private sealed class LaterClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => DateTimeOffset.FromUnixTimeMilliseconds(Now + 200_000);
    }

    private static DeviceRegistrySettings Trust(Fixture f) => new()
    {
        VaultId = "vault", Role = f.Grant.Role, AdminPublicKey = f.Admin.PublicKey,
        E2EGrant = EndToEndTrust.Sign(f.Grant, f.Admin)
    };
    private static string Proof(Fixture f, TrustChallenge challenge) => EndToEndTrust.Sign(
        new TrustFreshnessV2(2, "vault", f.Admin.DeviceId, f.Recipient.DeviceId, f.Grant.GrantId,
            1, challenge.Nonce, Now, Now + 30_000, false), f.Admin);
    private sealed class FixedClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => DateTimeOffset.FromUnixTimeMilliseconds(Now);
    }
    private sealed class MemoryJournal : IDeviceCommandJournal
    {
        private DeviceExecutionReceipt? receipt;
        public int Saves { get; private set; }
        public Action? AfterStarted { get; init; }
        public Task<DeviceExecutionReceipt?> ReadAsync(string vault, string device, string id, CancellationToken ct) => Task.FromResult(receipt);
        public Task SaveAsync(DeviceExecutionReceipt value, CancellationToken ct)
        {
            receipt = value;
            Saves++;
            if (value.State == DeviceExecutionState.Started) AfterStarted?.Invoke();
            return Task.CompletedTask;
        }
    }

    private sealed class Fixture : IDisposable
    {
        public Signer Admin { get; } = new();
        public Signer Recipient { get; } = new();
        public RecipientIdentity Identity => new(Recipient.DeviceId, Recipient.PublicKey, Recipient.EncryptionPublicKey, Recipient.EncryptionKeySignature);
        public DeviceGrantV2 Grant { get; }
        public DeviceCommandV2 Command { get; }
        public Fixture()
        {
            Grant = new(2, "vault", "invite", Admin.DeviceId, Recipient.DeviceId, Recipient.PublicKey,
                Recipient.EncryptionPublicKey, "TrustedOnline", ["ViewGuardCode"], ["76561198000000000"],
                Now - 1000, Now + 120_000, Guid.NewGuid().ToString("D"), 1, []);
            Command = new(2, "vault", Guid.NewGuid().ToString("D"), Admin.DeviceId, Recipient.DeviceId,
                "Lock", Now, Now + 60_000, "ManageDevices", Grant.GrantId, 1);
        }
        public void Dispose() { Admin.Dispose(); Recipient.Dispose(); }
    }

    private sealed class Signer : IDeviceRequestSigner, IDisposable
    {
        private readonly ECDsa ec = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        private readonly RSA rsa = RSA.Create(2048);
        public string PublicKey => EndToEndTrust.Encode(ec.ExportSubjectPublicKeyInfo());
        public string DeviceId => EndToEndTrust.DeviceId(PublicKey);
        public string EncryptionPublicKey => EndToEndTrust.Encode(rsa.ExportSubjectPublicKeyInfo());
        public string Protection => "synthetic-test-only";
        public string EncryptionKeySignature => Sign(Encoding.UTF8.GetBytes(
            $"sdapp-device-encryption-key/v1\ndeviceId={Encoding.UTF8.GetByteCount(DeviceId)}:{DeviceId}\npublicKey={Encoding.UTF8.GetByteCount(EncryptionPublicKey)}:{EncryptionPublicKey}\n"));
        public string Sign(byte[] data) => EndToEndTrust.Encode(ec.SignData(data, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence));
        public byte[] UnwrapTrustKey(byte[] wrapped) => rsa.Decrypt(wrapped, RSAEncryptionPadding.OaepSHA256);
        public void Dispose() { ec.Dispose(); rsa.Dispose(); }
    }
}
