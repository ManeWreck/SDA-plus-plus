namespace SDAPlusPlus.Core.Vault;

public interface IVaultAccountImportService
{
    VaultAccountImportResult Import(
        string sourceMaFile,
        string destinationDirectory,
        string? sourcePassword = null,
        string? destinationPassword = null);
}
