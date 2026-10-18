using System.Net;
using SDAPlusPlus.Core.Cloud;

namespace SDAPlusPlus.Core.Tests;

public class CloudReadTransportTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task LongServerCooldownIsRespectedInBothHeaderFormats(bool useDate)
    {
        var handler = new Handler((r, n, ct) =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
            response.Headers.RetryAfter = useDate
                ? new System.Net.Http.Headers.RetryConditionHeaderValue(DateTimeOffset.UtcNow.AddMinutes(2))
                : new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromMinutes(2));
            return Task.FromResult(response);
        });
        using var client = new HttpClient(handler);
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://cloud.test/file");
        using var result = await CloudReadTransport.SendAsync(client, request, "read");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, result.StatusCode);
        Assert.Equal(1, handler.Calls);
        Assert.Contains("Retry-After:", CloudReadTransport.DescribeFailure(result, "read"));
    }

    [Fact]
    public async Task RetriesTransientReadAndRetainsAuthorization()
    {
        var handler = new Handler((request, call, token) =>
        {
            Assert.Equal("Basic", request.Headers.Authorization?.Scheme);
            return Task.FromResult(new HttpResponseMessage(call == 1 ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.OK)
                { Content = new StringContent("encrypted test data") });
        });
        using var client = new HttpClient(handler);
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://cloud.test/manifest.json");
        request.Headers.Authorization = new("Basic", "test");
        using var response = await CloudReadTransport.SendAsync(client, request, "download manifest.json");
        Assert.Equal("encrypted test data", await response.Content.ReadAsStringAsync());
        Assert.Equal(2, handler.Calls);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.NotFound)]
    public async Task PermanentErrorsAreNotRetried(HttpStatusCode status)
    {
        var handler = new Handler((r, n, ct) => Task.FromResult(new HttpResponseMessage(status)));
        using var client = new HttpClient(handler);
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://cloud.test/file");
        using var response = await CloudReadTransport.SendAsync(client, request, "download file");
        Assert.Equal(status, response.StatusCode);
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task TimeoutIdentifiesOperationAfterBoundedRetry()
    {
        var handler = new Handler(async (r, n, ct) => { await Task.Delay(Timeout.Infinite, ct); return new(HttpStatusCode.OK); });
        using var client = new HttpClient(handler);
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://cloud.test/file");
        var error = await Assert.ThrowsAsync<HttpRequestException>(() =>
            CloudReadTransport.SendAsync(client, request, "download manifest.json", attemptTimeout: TimeSpan.FromMilliseconds(20)));
        Assert.Contains("download manifest.json", error.Message);
        Assert.Contains("2 attempts", error.Message);
        Assert.Equal(2, handler.Calls);
    }

    [Fact]
    public async Task CallerCancellationDoesNotRetry()
    {
        using var cancel = new CancellationTokenSource();
        var handler = new Handler((r, n, ct) => { cancel.Cancel(); ct.ThrowIfCancellationRequested(); return Task.FromResult(new HttpResponseMessage()); });
        using var client = new HttpClient(handler);
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://cloud.test/file");
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => CloudReadTransport.SendAsync(client, request, "read", cancel.Token));
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task WritesCannotBeRetriedByReadTransport()
    {
        using var client = new HttpClient(new Handler((r, n, ct) => throw new Exception("Must not send")));
        using var request = new HttpRequestMessage(HttpMethod.Put, "https://cloud.test/file");
        await Assert.ThrowsAsync<ArgumentException>(() => CloudReadTransport.SendAsync(client, request, "write"));
    }

    private sealed class Handler(Func<HttpRequestMessage, int, CancellationToken, Task<HttpResponseMessage>> action) : HttpMessageHandler
    {
        public int Calls { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => action(request, ++Calls, cancellationToken);
    }
}
