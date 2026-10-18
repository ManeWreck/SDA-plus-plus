namespace SDAPlusPlus.Core.Cloud;

public interface ICloudAuxiliaryFileProvider
{
    Task<CloudFileDownloadResult> DownloadFileAsync(
        CloudConnectionSettings settings, string fileName, CancellationToken cancellationToken = default) =>
        Task.FromResult(new CloudFileDownloadResult(false, "This provider does not support file downloads."));

    Task<CloudFileResult> UploadFileAsync(
        CloudConnectionSettings settings,
        string fileName,
        string contents,
        CancellationToken cancellationToken = default);
}

public sealed record CloudFileResult(bool Success, string Message);

public sealed record CloudFileDownloadResult(bool Success, string Message, string? Contents = null);
