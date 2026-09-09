namespace SDAPlusPlus.Core.Cloud;

public interface ICloudAuxiliaryFileProvider
{
    Task<CloudFileResult> UploadFileAsync(
        CloudConnectionSettings settings,
        string fileName,
        string contents,
        CancellationToken cancellationToken = default);
}

public sealed record CloudFileResult(bool Success, string Message);
