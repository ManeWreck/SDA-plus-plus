using SDAPlusPlus.Core.Cloud;

namespace SDAPlusPlus.Core.Vault;

public interface IVaultImportService
{
    VaultImportResult Commit(CloudPullPackage package, string maFilesDirectory);
}

public sealed record VaultImportResult(
    bool Success,
    string Message,
    int AccountCount = 0,
    string? BackupDirectory = null);
