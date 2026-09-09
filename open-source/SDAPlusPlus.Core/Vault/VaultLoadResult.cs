namespace SDAPlusPlus.Core.Vault;

public enum VaultLoadState
{
    Ready,
    Missing,
    Locked,
    InvalidPassword,
    Corrupt,
    Partial
}

public sealed record VaultLoadResult(
    VaultLoadState State,
    IReadOnlyList<VaultAccount> Accounts,
    string DirectoryPath,
    int SkippedFiles = 0,
    string? Detail = null)
{
    public bool IsReady => State is VaultLoadState.Ready or VaultLoadState.Partial;
    public bool RequiresPassword => State is VaultLoadState.Locked or VaultLoadState.InvalidPassword;
}
