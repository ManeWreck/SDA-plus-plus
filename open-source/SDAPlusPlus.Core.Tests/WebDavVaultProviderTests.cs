using System.Net;
using System.Text;
using SDAPlusPlus.Core.Cloud;

namespace SDAPlusPlus.Core.Tests;

public sealed class WebDavVaultProviderTests
{
    private static readonly CloudConnectionSettings Settings = new(
        "https://dav.example.test/root/",
        "account@example.test",
        "app-password",
        "SDA Vault");

    [Fact]
    public async Task RejectsNonHttpsEndpointWithoutSendingRequest()
    {
        var handler = new QueueHandler();
        var provider = new WebDavVaultProvider(new HttpClient(handler));

        CloudConnectionResult result = await provider.TestConnectionAsync(Settings with { Endpoint = "http://dav.example.test" });

        Assert.False(result.Success);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task TestConnectionUsesPropFindAndRequestScopedBasicAuth()
    {
        var handler = new QueueHandler(new HttpResponseMessage(HttpStatusCode.MultiStatus));
        var provider = new WebDavVaultProvider(new HttpClient(handler));

        CloudConnectionResult result = await provider.TestConnectionAsync(Settings);

        Assert.True(result.Success);
        CapturedRequest request = Assert.Single(handler.Requests);
        Assert.Equal("PROPFIND", request.Method);
        Assert.Equal("https://dav.example.test/root/SDA%20Vault/", request.Uri);
        Assert.StartsWith("Basic ", request.Authorization);
        Assert.DoesNotContain("app-password", request.Uri, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PreviewReadsManifestWithoutDownloadingAccountSecrets()
    {
        const string manifest = """
            {"encrypted":true,"entries":[
              {"filename":"76561198000000001.maFile"},
              {"filename":"../outside.maFile"}
            ]}
            """;
        var handler = new QueueHandler(
            new HttpResponseMessage(HttpStatusCode.MultiStatus),
            JsonResponse(HttpStatusCode.OK, manifest));
        var provider = new WebDavVaultProvider(new HttpClient(handler));

        CloudPullPreview result = await provider.PreviewPullAsync(Settings);

        Assert.True(result.Success);
        Assert.True(result.ManifestFound);
        Assert.True(result.IsEncrypted);
        Assert.Equal(1, result.AccountCount);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task PreviewFallsBackToPropFindWhenManifestIsMissing()
    {
        const string listing = """
            <?xml version="1.0"?>
            <d:multistatus xmlns:d="DAV:">
              <d:response><d:href>/root/SDA%20Vault/76561198000000001.maFile</d:href></d:response>
              <d:response><d:href>/root/SDA%20Vault/readme.txt</d:href></d:response>
            </d:multistatus>
            """;
        var handler = new QueueHandler(
            new HttpResponseMessage(HttpStatusCode.MultiStatus),
            new HttpResponseMessage(HttpStatusCode.NotFound),
            XmlResponse(HttpStatusCode.MultiStatus, listing));
        var provider = new WebDavVaultProvider(new HttpClient(handler));

        CloudPullPreview result = await provider.PreviewPullAsync(Settings);

        Assert.True(result.Success);
        Assert.False(result.ManifestFound);
        Assert.Equal(1, result.AccountCount);
        Assert.Equal("PROPFIND", handler.Requests[2].Method);
        Assert.Equal("1", handler.Requests[2].Depth);
    }

    [Fact]
    public async Task PreparePullDownloadsOnlyValidatedManifestAccounts()
    {
        const string manifest = """
            {"encrypted":false,"entries":[{"filename":"76561198000000001.maFile","steamid":76561198000000001}]}
            """;
        const string account = "{\"shared_secret\":\"MDEyMzQ1Njc4OWFiY2RlZg==\",\"account_name\":\"cloud-account\"}";
        var handler = new QueueHandler(
            new HttpResponseMessage(HttpStatusCode.MultiStatus),
            JsonResponse(HttpStatusCode.OK, manifest),
            JsonResponse(HttpStatusCode.OK, manifest),
            JsonResponse(HttpStatusCode.OK, account));
        var provider = new WebDavVaultProvider(new HttpClient(handler));

        CloudPullPackageResult result = await provider.PreparePullAsync(Settings);

        Assert.True(result.Success);
        Assert.NotNull(result.Package);
        Assert.Single(result.Package.AccountFiles);
        Assert.Equal(account, result.Package.AccountFiles["76561198000000001.maFile"]);
        Assert.Equal(4, handler.Requests.Count);
    }

    private static HttpResponseMessage JsonResponse(HttpStatusCode status, string content) => new(status)
    {
        Content = new StringContent(content, Encoding.UTF8, "application/json")
    };

    private static HttpResponseMessage XmlResponse(HttpStatusCode status, string content) => new(status)
    {
        Content = new StringContent(content, Encoding.UTF8, "application/xml")
    };

    private sealed class QueueHandler(params HttpResponseMessage[] responses) : HttpMessageHandler
    {
        private readonly Queue<HttpResponseMessage> _responses = new(responses);
        public List<CapturedRequest> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(new CapturedRequest(
                request.Method.Method,
                request.RequestUri?.AbsoluteUri ?? string.Empty,
                request.Headers.Authorization?.ToString() ?? string.Empty,
                request.Headers.TryGetValues("Depth", out IEnumerable<string>? values) ? values.Single() : string.Empty));
            return Task.FromResult(_responses.Dequeue());
        }
    }

    private sealed record CapturedRequest(string Method, string Uri, string Authorization, string Depth);
}
