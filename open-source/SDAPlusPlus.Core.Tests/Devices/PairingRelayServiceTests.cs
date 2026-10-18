using System.Net;
using System.Text;
using SDAPlusPlus.Core.Devices;

namespace SDAPlusPlus.Core.Tests.Devices;

public sealed class PairingRelayServiceTests
{
    [Fact]
    public async Task CheckHealth_AcceptsCompatibleWorker()
    {
        const string body = """
            {
              "ok": true,
              "protocol": "sdapp-relay",
              "protocolVersion": 1,
              "minimumClientProtocol": 1,
              "storage": "opaque-ciphertext-only",
              "capabilities": [
                "one-time-pairing",
                "device-registry-v1",
                "device-encryption-v1",
                "signed-action-grants-v1"
              ]
            }
            """;
        PairingRelayService service = CreateService(HttpStatusCode.OK, body);

        PairingRelayHealthResult result = await service.CheckHealthAsync("https://relay.example/");

        Assert.True(result.IsCompatible);
        Assert.Equal("https://relay.example", result.Endpoint);
        Assert.Equal("opaque-ciphertext-only", result.Storage);
    }

    [Fact]
    public async Task CheckHealth_RejectsIncompleteCapabilities()
    {
        PairingRelayService service = CreateService(HttpStatusCode.OK, """
            {"ok":true,"protocol":"sdapp-relay","protocolVersion":1,"minimumClientProtocol":1,
             "storage":"opaque-ciphertext-only","capabilities":["one-time-pairing"]}
            """);

        PairingRelayHealthResult result = await service.CheckHealthAsync("https://relay.example");

        Assert.False(result.IsCompatible);
    }

    private static PairingRelayService CreateService(HttpStatusCode status, string body) =>
        new(new HttpClient(new StubHandler(status, body)));

    private sealed class StubHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            });
    }
}
