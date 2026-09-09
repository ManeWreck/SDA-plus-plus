namespace SDAPlusPlus.Core.Cloud;

public interface ICloudAuthenticationProvider
{
    bool RequiresInteractiveAuthentication(CloudProviderKind provider);
    Task<CloudConnectionResult> AuthenticateAsync(
        CloudConnectionSettings settings,
        Action<string>? progress = null,
        CancellationToken cancellationToken = default);
}

internal interface IInteractiveCloudObjectStore
{
    Task<string> SignInAsync(Action<string>? progress, CancellationToken cancellationToken);
}
