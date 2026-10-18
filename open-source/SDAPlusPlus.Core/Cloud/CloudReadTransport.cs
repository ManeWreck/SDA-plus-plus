#nullable enable
using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace SDAPlusPlus.Core.Cloud;

/// <summary>Bounded, retryable reads. Never used for cloud writes.</summary>
public static class CloudReadTransport
{
    public static async Task<HttpResponseMessage> SendAsync(HttpClient client, HttpRequestMessage template,
        string operation, CancellationToken cancellationToken = default, TimeSpan? attemptTimeout = null)
    {
        if (template.Method != HttpMethod.Get && template.Method.Method != "PROPFIND")
            throw new ArgumentException("Only GET and PROPFIND can be retried.");
        byte[]? body = template.Content == null ? null : await template.Content.ReadAsByteArrayAsync(cancellationToken);
        var timeout = attemptTimeout ?? TimeSpan.FromSeconds(30);
        const int maximumBytes = 8 * 1024 * 1024;
        for (int attempt = 1; ; attempt++)
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(timeout);
            using var request = new HttpRequestMessage(template.Method, template.RequestUri);
            foreach (var header in template.Headers) request.Headers.TryAddWithoutValidation(header.Key, header.Value);
            if (body != null)
            {
                request.Content = new ByteArrayContent(body);
                foreach (var header in template.Content!.Headers)
                {
                    request.Content.Headers.Remove(header.Key);
                    request.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
                }
            }
            HttpResponseMessage? response = null;
            try
            {
                response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token);
                if (attempt == 1 && (response.StatusCode is HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests
                    or HttpStatusCode.BadGateway or HttpStatusCode.ServiceUnavailable or HttpStatusCode.GatewayTimeout))
                {
                    var delay = response.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(1);
                    // Do not hammer a provider that explicitly asks for a long cooldown.
                    if (delay > TimeSpan.FromSeconds(5)) return response;
                    response.Dispose();
                    response = null;
                    await Task.Delay(delay > TimeSpan.Zero ? delay : TimeSpan.Zero, cancellationToken);
                    continue;
                }
                using var stream = await response.Content.ReadAsStreamAsync(deadline.Token);
                using var buffer = new MemoryStream();
                byte[] chunk = new byte[8192];
                int count;
                while ((count = await stream.ReadAsync(chunk, deadline.Token)) > 0)
                {
                    if (buffer.Length + count > maximumBytes)
                        throw new HttpRequestException($"WebDAV: {operation}: response exceeds 8 MiB.");
                    buffer.Write(chunk, 0, count);
                }
                var content = new ByteArrayContent(buffer.ToArray());
                foreach (var header in response.Content.Headers)
                    content.Headers.TryAddWithoutValidation(header.Key, header.Value);
                response.Content.Dispose();
                response.Content = content;
                return response;
            }
            catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
            {
                response?.Dispose();
                if (attempt >= 2)
                    throw new HttpRequestException($"WebDAV: {operation}: no complete response within {timeout.TotalSeconds:0} seconds per attempt (2 attempts). Check the cloud address, network/VPN and provider availability. Local files were not replaced by this download.", ex);
            }
            catch (HttpRequestException ex)
            {
                response?.Dispose();
                if (attempt >= 2 || ex.Message.Contains("exceeds 8 MiB"))
                    throw new HttpRequestException($"WebDAV: {operation}: connection failed ({ex.GetType().Name}). Check the cloud address and network.", ex);
            }
            catch { response?.Dispose(); throw; }
            await Task.Delay(TimeSpan.FromMilliseconds(300), cancellationToken);
        }
    }
}
