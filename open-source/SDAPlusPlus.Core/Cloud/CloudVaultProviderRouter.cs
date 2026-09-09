namespace SDAPlusPlus.Core.Cloud;

public sealed class CloudVaultProviderRouter(HttpClient webDavClient, string secretDirectory) : ICloudVaultProvider, ICloudAuthenticationProvider, ICloudAuxiliaryFileProvider
{
    private readonly CloudSecretStore _secrets = new(secretDirectory);
    private readonly WebDavVaultProvider _webDav = new(webDavClient);
    private readonly ObjectStoreVaultProvider _s3 = new(settings => new S3ObjectStore(settings));
    private readonly ObjectStoreVaultProvider _dropbox = new(settings => new DropboxObjectStore(settings));

    public Task<CloudConnectionResult> TestConnectionAsync(CloudConnectionSettings settings, CancellationToken cancellationToken = default) =>
        Resolve(settings).TestConnectionAsync(settings, cancellationToken);

    public Task<CloudPullPreview> PreviewPullAsync(CloudConnectionSettings settings, CancellationToken cancellationToken = default) =>
        Resolve(settings).PreviewPullAsync(settings, cancellationToken);

    public Task<CloudPullPackageResult> PreparePullAsync(CloudConnectionSettings settings, CancellationToken cancellationToken = default) =>
        Resolve(settings).PreparePullAsync(settings, cancellationToken);

    public Task<CloudPushResult> PushAsync(CloudConnectionSettings settings, CloudPushPackage package, CancellationToken cancellationToken = default) =>
        Resolve(settings).PushAsync(settings, package, cancellationToken);

    public bool RequiresInteractiveAuthentication(CloudProviderKind provider) =>
        provider is CloudProviderKind.OneDrivePersonal or CloudProviderKind.GoogleDrive;

    public async Task<CloudConnectionResult> AuthenticateAsync(CloudConnectionSettings settings, Action<string>? progress = null, CancellationToken cancellationToken = default)
    {
        try
        {
            using ICloudObjectStore store = CreateOAuthStore(settings);
            string message = await ((IInteractiveCloudObjectStore)store).SignInAsync(progress, cancellationToken);
            return new CloudConnectionResult(true, message);
        }
        catch (Exception ex) when (ex is InvalidOperationException or HttpRequestException or Microsoft.Identity.Client.MsalException or TaskCanceledException)
        {
            return new CloudConnectionResult(false, ex.Message);
        }
    }

    public async Task<CloudFileResult> UploadFileAsync(
        CloudConnectionSettings settings,
        string fileName,
        string contents,
        CancellationToken cancellationToken = default)
    {
        if (!string.Equals(fileName, "credentials.secure.json", StringComparison.OrdinalIgnoreCase))
            return new CloudFileResult(false, "Unsupported cloud file name.");
        if (settings.Provider == CloudProviderKind.WebDav)
            return await _webDav.UploadFileAsync(settings, fileName, contents, cancellationToken);

        try
        {
            using ICloudObjectStore store = CreateObjectStore(settings);
            await store.EnsureContainerAsync(cancellationToken);
            await store.UploadAsync(fileName, contents, cancellationToken);
            return new CloudFileResult(true, fileName + " uploaded securely.");
        }
        catch (Exception ex) when (ex is InvalidOperationException or HttpRequestException or Amazon.S3.AmazonS3Exception or Microsoft.Identity.Client.MsalException or TaskCanceledException)
        {
            return new CloudFileResult(false, ex.Message);
        }
    }

    private ICloudVaultProvider Resolve(CloudConnectionSettings settings) => settings.Provider switch
    {
        CloudProviderKind.WebDav => _webDav,
        CloudProviderKind.S3Compatible => _s3,
        CloudProviderKind.Dropbox => _dropbox,
        CloudProviderKind.OneDrivePersonal => new ObjectStoreVaultProvider(value => new OneDriveObjectStore(value, _secrets)),
        CloudProviderKind.GoogleDrive => new ObjectStoreVaultProvider(value => new GoogleDriveObjectStore(value, _secrets)),
        _ => throw new InvalidOperationException("Unsupported cloud provider.")
    };

    private ICloudObjectStore CreateOAuthStore(CloudConnectionSettings settings) => settings.Provider switch
    {
        CloudProviderKind.OneDrivePersonal => new OneDriveObjectStore(settings, _secrets),
        CloudProviderKind.GoogleDrive => new GoogleDriveObjectStore(settings, _secrets),
        _ => throw new InvalidOperationException("The selected provider does not use interactive OAuth.")
    };

    private ICloudObjectStore CreateObjectStore(CloudConnectionSettings settings) => settings.Provider switch
    {
        CloudProviderKind.S3Compatible => new S3ObjectStore(settings),
        CloudProviderKind.Dropbox => new DropboxObjectStore(settings),
        CloudProviderKind.OneDrivePersonal => new OneDriveObjectStore(settings, _secrets),
        CloudProviderKind.GoogleDrive => new GoogleDriveObjectStore(settings, _secrets),
        _ => throw new InvalidOperationException("The selected provider does not support object storage.")
    };
}
