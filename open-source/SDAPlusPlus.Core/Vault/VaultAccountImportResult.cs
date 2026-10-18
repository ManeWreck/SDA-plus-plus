namespace SDAPlusPlus.Core.Vault;

public enum VaultAccountImportState
{
    Imported,
    SourceMissing,
    SourceLocked,
    InvalidSourcePassword,
    InvalidAccount,
    DestinationLocked,
    InvalidDestinationPassword,
    Duplicate,
    DestinationCorrupt,
    Failed
}

public sealed record VaultAccountImportResult(
    VaultAccountImportState State,
    string? AccountName = null,
    string? BackupDirectory = null,
    string? Detail = null)
{
    public bool IsSuccess => State == VaultAccountImportState.Imported;
}
