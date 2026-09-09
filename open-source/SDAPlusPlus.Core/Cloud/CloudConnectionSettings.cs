namespace SDAPlusPlus.Core.Cloud;

public enum CloudProviderKind
{
    WebDav,
    S3Compatible,
    Dropbox,
    OneDrivePersonal,
    GoogleDrive
}

public sealed record CloudConnectionSettings(
    string Endpoint,
    string Username,
    string Password,
    string RemotePath,
    CloudProviderKind Provider = CloudProviderKind.WebDav,
    string Container = "",
    string ClientId = "");
