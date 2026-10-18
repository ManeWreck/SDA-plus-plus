using SDAPlusPlus.Core.Devices;

namespace SDAPlusPlus.Core.Tests.Devices;

public sealed class DeviceCommandProcessorTests
{
    private static IncomingDeviceCommand Command() => new("vault", "device", Guid.NewGuid().ToString("D"), "Lock", long.MaxValue);

    [Fact]
    public async Task DuplicateDeliveryExecutesOnlyOnce()
    {
        var journal = new Journal();
        var processor = new DeviceCommandProcessor(journal);
        var command = Command();
        int calls = 0;
        async Task<bool> Execute(IncomingDeviceCommand c, CancellationToken ct)
        { Assert.Equal(DeviceExecutionState.Started, journal.Value!.State); calls++; await Task.Yield(); return true; }
        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => processor.ProcessAsync(command, "vault", "device", Execute)));
        Assert.Equal(1, calls);
        Assert.All(results, result => Assert.Equal(DeviceExecutionState.Completed, result.State));
    }

    [Fact]
    public async Task RestartWithStartedCheckpointDoesNotReplay()
    {
        var command = Command();
        var journal = new Journal { Value = new(command, DeviceExecutionState.Started) };
        var result = await new DeviceCommandProcessor(journal).ProcessAsync(command, "vault", "device", NeverExecute);
        Assert.Equal(DeviceExecutionState.OutcomeUnknown, result.State);
    }

    [Fact]
    public async Task FailureToSaveCheckpointPreventsExecution()
    {
        var journal = new Journal { FailSave = 1 };
        await Assert.ThrowsAsync<IOException>(() => new DeviceCommandProcessor(journal).ProcessAsync(Command(), "vault", "device", NeverExecute));
    }

    [Fact]
    public async Task FailureToSaveResultDoesNotReplayAfterRestart()
    {
        var command = Command();
        var journal = new Journal { FailSave = 2 };
        await Assert.ThrowsAsync<IOException>(() => new DeviceCommandProcessor(journal).ProcessAsync(command, "vault", "device", (_, _) => Task.FromResult(true)));
        var result = await new DeviceCommandProcessor(journal).ProcessAsync(command, "vault", "device", NeverExecute);
        Assert.Equal(DeviceExecutionState.OutcomeUnknown, result.State);
    }

    [Fact]
    public async Task ExpiredCommandDoesNotExecute()
    {
        var result = await new DeviceCommandProcessor(new Journal()).ProcessAsync(Command() with { ExpiresAt = 1 }, "vault", "device", NeverExecute);
        Assert.Equal(DeviceExecutionState.Expired, result.State);
    }

    [Fact]
    public async Task ReusedIdWithChangedContentIsRejected()
    {
        var command = Command();
        var journal = new Journal { Value = new(command, DeviceExecutionState.Completed) };
        await Assert.ThrowsAsync<InvalidOperationException>(() => new DeviceCommandProcessor(journal).ProcessAsync(command with { Type = "WipeLocalData" }, "vault", "device", NeverExecute));
    }

    [Theory]
    [InlineData("other", "device", "Lock")]
    [InlineData("vault", "other", "Lock")]
    [InlineData("vault", "device", "Unknown")]
    public async Task WrongTargetOrTypeIsRejected(string vault, string device, string type)
    {
        await Assert.ThrowsAsync<ArgumentException>(() => new DeviceCommandProcessor(new Journal()).ProcessAsync(Command() with { VaultId = vault, DeviceId = device, Type = type }, "vault", "device", NeverExecute));
    }

    [Fact]
    public async Task ExecutionExceptionIsUnknownAndNotRetried()
    {
        var journal = new Journal();
        var processor = new DeviceCommandProcessor(journal);
        var command = Command();
        var result = await processor.ProcessAsync(command, "vault", "device", (_, _) => throw new OperationCanceledException());
        Assert.Equal(DeviceExecutionState.OutcomeUnknown, result.State);
        Assert.Equal(result, await processor.ProcessAsync(command, "vault", "device", NeverExecute));
    }

    // Returning success makes accidental execution observable as Completed, rather than
    // throwing an assertion that the processor would correctly classify as OutcomeUnknown.
    private static Task<bool> NeverExecute(IncomingDeviceCommand command, CancellationToken token) => Task.FromResult(true);

    [Fact]
    public async Task FileJournalSurvivesRestartAndRejectsCorruption()
    {
        if (!OperatingSystem.IsWindows()) return;
        string directory = Path.Combine(Path.GetTempPath(), "sdapp-command-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            var command = Command();
            var journal = new WindowsDeviceCommandJournal(directory);
            await journal.SaveAsync(new(command, DeviceExecutionState.Started), CancellationToken.None);
            var restarted = new WindowsDeviceCommandJournal(directory);
            Assert.Equal(DeviceExecutionState.Started, (await restarted.ReadAsync("vault", "device", command.CommandId, CancellationToken.None))!.State);
            var result = await new DeviceCommandProcessor(restarted).ProcessAsync(command, "vault", "device", NeverExecute);
            Assert.Equal(DeviceExecutionState.OutcomeUnknown, result.State);
            File.WriteAllBytes(Directory.GetFiles(directory, "*.receipt").Single(), [1, 2, 3]);
            await Assert.ThrowsAsync<System.Security.Cryptography.CryptographicException>(() => restarted.SaveAsync(new(command, DeviceExecutionState.Completed), CancellationToken.None));
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    private sealed class Journal : IDeviceCommandJournal
    {
        public DeviceExecutionReceipt? Value;
        public int FailSave;
        private int _saves;
        public Task<DeviceExecutionReceipt?> ReadAsync(string vault, string device, string id, CancellationToken ct) => Task.FromResult(Value);
        public Task SaveAsync(DeviceExecutionReceipt receipt, CancellationToken ct)
        {
            if (++_saves == FailSave) throw new IOException("Synthetic persistence failure");
            Value = receipt;
            return Task.CompletedTask;
        }
    }
}
