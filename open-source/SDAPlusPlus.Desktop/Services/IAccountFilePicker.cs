namespace SDAPlusPlus.Desktop.Services;

public interface IAccountFilePicker
{
    Task<string?> PickMaFileAsync();
    Task<string?> PickCredentialsFileAsync();
    Task<string?> PickPlainCredentialsFileAsync();
}
