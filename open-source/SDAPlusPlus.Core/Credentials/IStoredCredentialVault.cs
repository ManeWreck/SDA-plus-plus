using SDAPlusPlus.Core.Vault;

namespace SDAPlusPlus.Core.Credentials;

public interface IStoredCredentialVault
{
    CredentialVaultLoadResult Load();
    bool TryGet(VaultAccount account, out StoredCredential? credential);
    CredentialVaultWriteResult Save(VaultAccount account, string username, string password);
    CredentialVaultWriteResult CreateEncrypted(string encryptionKey);
    CredentialVaultWriteResult ImportEncrypted(string sourcePath, string encryptionKey);
    CredentialVaultWriteResult ImportPlaintext(string sourcePath);
    CredentialVaultWriteResult DeleteVault();
    CredentialVaultWriteResult Remove(VaultAccount account);
    CredentialVaultWriteResult Remove(StoredCredential credential);
    CredentialVaultWriteResult UpgradeEncryption() => new(CredentialVaultState.Invalid, "Authenticated migration is not supported by this vault.");
}

public enum CredentialVaultState
{
    Ready,
    Missing,
    Locked,
    Invalid,
    Failed
}

public sealed record StoredCredential(
    ulong SteamId,
    string AccountName,
    string Username,
    string Password,
    DateTime UpdatedUtc);

public sealed record CredentialVaultLoadResult(
    CredentialVaultState State,
    IReadOnlyList<StoredCredential> Credentials,
    string Path,
    bool Encrypted,
    string? Detail = null)
{
    public bool IsReady => State == CredentialVaultState.Ready;
}

public sealed record CredentialVaultWriteResult(CredentialVaultState State, string? Detail = null, string? BackupPath = null)
{
    public bool IsSuccess => State == CredentialVaultState.Ready;
}
