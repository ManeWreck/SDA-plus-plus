namespace SDAPlusPlus.Core.Vault;

public interface IVaultAccountWriter
{
    VaultWriteResult SaveSession(string maFilesDirectory, VaultAccount account, string? vaultPassword = null);

    VaultWriteResult SetFavorite(string maFilesDirectory, VaultAccount account, bool favorite);
}
