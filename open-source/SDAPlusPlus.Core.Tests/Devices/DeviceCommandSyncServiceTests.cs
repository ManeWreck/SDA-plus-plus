using System.Reflection;
using SDAPlusPlus.Core.Devices;

namespace SDAPlusPlus.Core.Tests.Devices;

public sealed class DeviceCommandSyncServiceTests
{
    [Theory]
    [InlineData("Admin", true)]
    [InlineData("TrustedOnline", true)]
    [InlineData("TrustedOffline", true)]
    [InlineData("TrustedOnline", false)]
    public async Task LegacyEntryPointCannotFetchExecuteJournalOrAcknowledge(string role, bool valid)
    {
        var client = DispatchProxy.Create<IDeviceRegistryClient, RejectCalls>();
        var journal = DispatchProxy.Create<IDeviceCommandJournal, RejectCalls>();
        var service = new DeviceCommandSyncService(client, new(journal));
        int executions = 0;
        var result = await service.SyncAsync(new()
        {
            VaultId = "vault", RelayUrl = "https://relay.example", Role = role
        }, "local", () => valid, (_, _) =>
        {
            executions++;
            return Task.FromResult(true);
        });
        Assert.False(result.Success);
        Assert.Empty(result.Items);
        Assert.Contains("disabled", result.Detail);
        Assert.Equal(0, executions);
    }

    [Fact]
    public async Task CancellationDoesNotReachDependencies()
    {
        var service = new DeviceCommandSyncService(
            DispatchProxy.Create<IDeviceRegistryClient, RejectCalls>(),
            new(DispatchProxy.Create<IDeviceCommandJournal, RejectCalls>()));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.SyncAsync(new(), "local",
            () => throw new InvalidOperationException("Must not inspect identity"),
            (_, _) => throw new InvalidOperationException("Must not execute"), cancellation.Token));
    }

    public class RejectCalls : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
            throw new InvalidOperationException($"Disabled sync called {targetMethod?.Name}");
    }
}
