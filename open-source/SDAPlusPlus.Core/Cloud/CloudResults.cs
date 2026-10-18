namespace SDAPlusPlus.Core.Cloud;

public sealed record CloudConnectionResult(bool Success, string Message);

public sealed record CloudPullPreview(
    bool Success,
    string Message,
    int AccountCount = 0,
    bool IsEncrypted = false,
    bool ManifestFound = false,
    IReadOnlyList<string>? FileNames = null);

public sealed record CloudPullPackage(
    string ManifestJson,
    IReadOnlyDictionary<string, string> AccountFiles,
    bool IsEncrypted);

public sealed record CloudPullPackageResult(
    bool Success,
    string Message,
    CloudPullPackage? Package = null);

public sealed record CloudPushPackage(
    string ManifestJson,
    IReadOnlyDictionary<string, string> AccountFiles,
    bool IsEncrypted);

public sealed record CloudPushResult(bool Success, string Message, int AccountCount = 0);

public sealed record CloudPushPackageResult(bool Success, string Message, CloudPushPackage? Package = null);
