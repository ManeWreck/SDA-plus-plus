namespace SDAPlusPlus.Core.Vault;

public enum VaultWriteState
{
    Saved,
    Missing,
    Locked,
    InvalidPassword,
    AccountNotFound,
    Corrupt,
    Failed
}

public sealed record VaultWriteResult(VaultWriteState State, string? BackupDirectory = null, string? Detail = null)
{
    public bool IsSuccess => State == VaultWriteState.Saved;
}
