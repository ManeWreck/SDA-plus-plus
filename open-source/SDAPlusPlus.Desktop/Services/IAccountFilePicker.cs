namespace SDAPlusPlus.Desktop.Services;

public interface IAccountFilePicker
{
    Task<string?> PickVaultFolderAsync() => Task.FromResult<string?>(null);
    Task<string?> PickMaFileAsync();
    Task<string?> PickCredentialsFileAsync();
    Task<string?> PickPlainCredentialsFileAsync();
}
