namespace SDAPlusPlus.Core.Vault;

public interface IVaultEncryptionService
{
    VaultEncryptionStatus Inspect(string maFilesDirectory);

    VaultEncryptionResult ChangeProtection(
        string maFilesDirectory,
        string? currentPassword,
        string? newPassword);
}

public sealed record VaultEncryptionStatus(
    bool Exists,
    bool IsEncrypted,
    int AccountCount,
    string? Detail = null);

public enum VaultEncryptionState
{
    Changed,
    Missing,
    Corrupt,
    InvalidPassword,
    InvalidRequest,
    Failed
}

public sealed record VaultEncryptionResult(
    VaultEncryptionState State,
    bool IsEncrypted,
    int AccountCount = 0,
    string? BackupDirectory = null,
    string? Detail = null)
{
    public bool Success => State == VaultEncryptionState.Changed;
}
