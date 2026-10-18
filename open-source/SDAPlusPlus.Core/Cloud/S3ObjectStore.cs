using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using System.Net;

namespace SDAPlusPlus.Core.Cloud;

internal sealed class S3ObjectStore : ICloudObjectStore
{
    private readonly AmazonS3Client _client;
    private readonly string _bucket;
    private readonly string _prefix;

    public S3ObjectStore(CloudConnectionSettings settings)
    {
        Require(settings.Endpoint, "S3 endpoint");
        Require(settings.Container, "S3 bucket");
        Require(settings.Username, "S3 access key");
        Require(settings.Password, "S3 secret key");
        if (!Uri.TryCreate(settings.Endpoint, UriKind.Absolute, out Uri? endpoint) || endpoint.Scheme != Uri.UriSchemeHttps)
            throw new InvalidOperationException("A valid HTTPS S3 endpoint is required.");
        _bucket = settings.Container.Trim();
        _prefix = NormalizePrefix(settings.RemotePath);
        _client = new AmazonS3Client(new BasicAWSCredentials(settings.Username.Trim(), settings.Password), new AmazonS3Config
        {
            ServiceURL = endpoint.GetLeftPart(UriPartial.Authority),
            ForcePathStyle = true,
            AuthenticationRegion = InferRegion(endpoint)
        });
    }

    public async Task<string> TestAsync(CancellationToken cancellationToken)
    {
        await _client.ListObjectsV2Async(new ListObjectsV2Request { BucketName = _bucket, Prefix = _prefix, MaxKeys = 1 }, cancellationToken);
        return "S3-compatible storage connection established.";
    }

    public Task EnsureContainerAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public async Task<IReadOnlyList<string>> ListAsync(CancellationToken cancellationToken)
    {
        List<string> names = [];
        string? continuation = null;
        do
        {
            ListObjectsV2Response response = await _client.ListObjectsV2Async(new ListObjectsV2Request
            {
                BucketName = _bucket, Prefix = _prefix, ContinuationToken = continuation
            }, cancellationToken);
            names.AddRange(response.S3Objects.Select(item => item.Key[_prefix.Length..]).Where(value => !value.Contains('/')));
            continuation = response.IsTruncated ? response.NextContinuationToken : null;
        } while (continuation is not null);
        return names;
    }

    public async Task<string?> DownloadAsync(string fileName, bool optional, CancellationToken cancellationToken)
    {
        try
        {
            using GetObjectResponse response = await _client.GetObjectAsync(_bucket, _prefix + fileName, cancellationToken);
            using StreamReader reader = new(response.ResponseStream);
            return await reader.ReadToEndAsync(cancellationToken);
        }
        catch (AmazonS3Exception ex) when (optional && ex.StatusCode == HttpStatusCode.NotFound) { return null; }
    }

    public Task UploadAsync(string fileName, string contents, CancellationToken cancellationToken) =>
        _client.PutObjectAsync(new PutObjectRequest
        {
            BucketName = _bucket, Key = _prefix + fileName, ContentBody = contents, ContentType = "application/json"
        }, cancellationToken);

    public void Dispose() => _client.Dispose();
    private static string NormalizePrefix(string value) => string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim().Trim('/') + "/";
    private static string InferRegion(Uri endpoint) => endpoint.Host.Contains("r2.cloudflarestorage.com", StringComparison.OrdinalIgnoreCase) ? "auto" : "us-east-1";
    private static void Require(string value, string name) { if (string.IsNullOrWhiteSpace(value)) throw new InvalidOperationException(name + " is required."); }
}
