using System.Net;
using System.Text;
using SDAPlusPlus.Core.Devices;

namespace SDAPlusPlus.Core.Tests.Devices;

public sealed class DeviceCommandSyncServiceTests
{
    [Fact]
    public async Task LostAcknowledgmentRetriesOnlyAcknowledgmentAfterRestart()
    {
        var handler = new Handler();
        var client = new DeviceRegistryClient(new HttpClient(handler), new Signer());
        var journal = new Journal();
        int executions = 0;
        Task<bool> Execute(IncomingDeviceCommand command, CancellationToken token) { executions++; return Task.FromResult(true); }
        var first = await new DeviceCommandSyncService(client, new(journal)).SyncAsync(Settings(), "local", () => true, Execute);
        Assert.False(Assert.Single(first.Items).Acknowledged);
        Assert.False(first.Success);
        handler.FailAck = false;
        var second = await new DeviceCommandSyncService(client, new(journal)).SyncAsync(Settings(), "local", () => true, Execute);
        Assert.True(Assert.Single(second.Items).Acknowledged);
        Assert.True(second.Success);
        Assert.Equal(1, executions);
        Assert.Equal(2, handler.Acks);
    }

    [Fact]
    public async Task UnknownOutcomeNeverAcknowledgedAsFailure()
    {
        var handler = new Handler();
        var client = new DeviceRegistryClient(new HttpClient(handler), new Signer());
        var sync = new DeviceCommandSyncService(client, new(new Journal()));
        var result = await sync.SyncAsync(Settings(), "local", () => true, (_, _) => throw new IOException("Synthetic ambiguous failure"));
        Assert.False(result.Success);
        Assert.Equal(DeviceExecutionState.OutcomeUnknown, Assert.Single(result.Items).Receipt.State);
        Assert.Equal(0, handler.Acks);
    }

    [Fact]
    public async Task IdentityChangeAfterFetchPreventsExecution()
    {
        bool valid = true;
        var handler = new Handler { OnFetch = () => valid = false };
        var client = new DeviceRegistryClient(new HttpClient(handler), new Signer());
        int executions = 0;
        var result = await new DeviceCommandSyncService(client, new(new Journal())).SyncAsync(Settings(), "local", () => valid,
            (_, _) => { executions++; return Task.FromResult(true); });
        Assert.False(result.Success);
        Assert.Equal(0, executions);
        Assert.Equal(0, handler.Acks);
    }

    [Fact]
    public async Task ExpiredCommandIsNotExecutedOrMisreportedToLegacyServer()
    {
        var handler = new Handler { Expiry = 1 };
        var client = new DeviceRegistryClient(new HttpClient(handler), new Signer());
        int executions = 0;
        var result = await new DeviceCommandSyncService(client, new(new Journal())).SyncAsync(Settings(), "local", () => true,
            (_, _) => { executions++; return Task.FromResult(true); });
        Assert.Equal(DeviceExecutionState.Expired, Assert.Single(result.Items).Receipt.State);
        Assert.Equal(0, executions);
        Assert.Equal(0, handler.Acks);
    }

    private static DeviceRegistrySettings Settings() => new() { VaultId = "vault", RelayUrl = "https://relay.example", Role = "TrustedOnline" };
    private sealed class Journal : IDeviceCommandJournal
    {
        private DeviceExecutionReceipt? _receipt;
        public Task<DeviceExecutionReceipt?> ReadAsync(string v, string d, string id, CancellationToken ct) => Task.FromResult(_receipt);
        public Task SaveAsync(DeviceExecutionReceipt receipt, CancellationToken ct) { _receipt = receipt; return Task.CompletedTask; }
    }
    private sealed class Signer : IDeviceRequestSigner
    {
        public string DeviceId => "local";
        public string PublicKey => "synthetic";
        public string EncryptionPublicKey => "synthetic";
        public string EncryptionKeySignature => "synthetic";
        public string Protection => "test";
        public string Sign(byte[] data) => "synthetic";
    }
    private sealed class Handler : HttpMessageHandler
    {
        public bool FailAck = true;
        public int Acks;
        public long Expiry = long.MaxValue;
        public Action? OnFetch;
        private readonly string _id = Guid.NewGuid().ToString("D");
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Method == HttpMethod.Get)
            {
                OnFetch?.Invoke();
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent($"{{\"commands\":[{{\"commandId\":\"{_id}\",\"type\":\"Lock\",\"expiresAt\":{Expiry}}}]}}", Encoding.UTF8, "application/json") });
            }
            Acks++;
            return Task.FromResult(new HttpResponseMessage(FailAck ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.OK) { Content = new StringContent("{}") });
        }
    }
}
