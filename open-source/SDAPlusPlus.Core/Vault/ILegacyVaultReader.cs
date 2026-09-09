namespace SDAPlusPlus.Core.Vault;

public interface ILegacyVaultReader
{
    VaultLoadResult Load(string maFilesDirectory, string? password = null);
}
