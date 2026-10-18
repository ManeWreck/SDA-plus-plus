namespace SDAPlusPlus.Core.Cloud;

internal interface ICloudObjectStore : IDisposable
{
    Task<string> TestAsync(CancellationToken cancellationToken);
    Task EnsureContainerAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<string>> ListAsync(CancellationToken cancellationToken);
    Task<string?> DownloadAsync(string fileName, bool optional, CancellationToken cancellationToken);
    Task UploadAsync(string fileName, string contents, CancellationToken cancellationToken);
}
