namespace SDAPlusPlus.Core.Cloud;

public interface ICloudVaultProvider
{
    Task<CloudConnectionResult> TestConnectionAsync(CloudConnectionSettings settings, CancellationToken cancellationToken = default);
    Task<CloudPullPreview> PreviewPullAsync(CloudConnectionSettings settings, CancellationToken cancellationToken = default);
    Task<CloudPullPackageResult> PreparePullAsync(CloudConnectionSettings settings, CancellationToken cancellationToken = default);
    Task<CloudPushResult> PushAsync(CloudConnectionSettings settings, CloudPushPackage package, CancellationToken cancellationToken = default);
}
