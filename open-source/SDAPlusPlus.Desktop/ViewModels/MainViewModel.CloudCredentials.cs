using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SDAPlusPlus.Core.Cloud;
using System.IO;

namespace SDAPlusPlus.Desktop.ViewModels;

public partial class MainViewModel
{
    [ObservableProperty] private bool _isCredentialDownloadBusy;
    [ObservableProperty] private bool _isCredentialCloudAuthVisible;
    [ObservableProperty] private string _credentialCloudPassword = string.Empty;
    private CloudConnectionSettings? _pendingCredentialConnection;
    private string? _downloadedCredentialPath;
    private bool _resumeAutoLoginAfterCredentialImport;
    public string DownloadCredentialsLabel => IsRussian ? "Скачать логины из облака" : "Download credentials from cloud";
    public string CredentialCloudAuthHint => IsRussian
        ? "Введите пароль приложения WebDAV, секретный ключ S3 или токен Dropbox для выбранного облака. Это не ключ файла логинов."
        : "Enter the WebDAV app password, S3 secret key or Dropbox token for the selected cloud. This is not the credentials file key.";

    [RelayCommand]
    private async Task ConfirmCredentialCloudAuthAsync()
    {
        if (_pendingCredentialConnection is null || string.IsNullOrEmpty(CredentialCloudPassword)) return;
        _pendingCredentialConnection = _pendingCredentialConnection with { Password = CredentialCloudPassword };
        CredentialCloudPassword = string.Empty;
        IsCredentialCloudAuthVisible = false;
        await DownloadCredentialsAsync();
    }

    [RelayCommand]
    private void CancelCredentialCloudAuth()
    {
        _pendingCredentialConnection = null;
        CredentialCloudPassword = string.Empty;
        IsCredentialCloudAuthVisible = false;
        _resumeAutoLoginAfterCredentialImport = false;
    }

    [RelayCommand]
    private async Task DownloadCredentialsAsync()
    {
        if (IsCredentialDownloadBusy || IsCredentialImportVisible || IsCredentialCloudAuthVisible) return;
        if (_cloudProvider is not ICloudAuxiliaryFileProvider provider)
        {
            CredentialsStatus = IsRussian ? "Этот провайдер не поддерживает скачивание логинов." : "This provider cannot download credentials.";
            return;
        }
        var settings = _pendingCredentialConnection ?? CreateCloudSettings();
        bool pairedProfileMatches = settings.Provider == CloudProviderKind.WebDav &&
            settings.Endpoint == _pairedCloudEndpoint && settings.Username == _pairedCloudUsername;
        if (pairedProfileMatches && !string.IsNullOrWhiteSpace(_pairedCredentialsPath))
            settings = settings with { RemotePath = _pairedCredentialsPath };
        if (settings.Provider is CloudProviderKind.WebDav or CloudProviderKind.S3Compatible or CloudProviderKind.Dropbox &&
            string.IsNullOrEmpty(settings.Password))
        {
            _pendingCredentialConnection = settings;
            CredentialCloudPassword = string.Empty;
            IsCredentialCloudAuthVisible = true;
            return;
        }
        _pendingCredentialConnection = null;
        IsCredentialDownloadBusy = true;
        CredentialsStatus = IsRussian ? "Загрузка зашифрованных логинов..." : "Downloading encrypted credentials...";
        try
        {
            var result = await provider.DownloadFileAsync(settings, "credentials.secure.json");
            if (!result.Success || result.Contents is null)
            {
                CredentialsStatus = result.Message;
                return;
            }
            // Stage ciphertext only; the existing vault is untouched until the key is verified.
            Directory.CreateDirectory(_maFilesDirectory);
            CleanupDownloadedCredentials();
            _downloadedCredentialPath = Path.Combine(_maFilesDirectory, ".credentials-download-" + Guid.NewGuid().ToString("N") + ".json");
            await File.WriteAllTextAsync(_downloadedCredentialPath, result.Contents);
            CredentialImportSourcePath = _downloadedCredentialPath;
            CredentialImportKey = pairedProfileMatches ? _pairedCredentialsKey : string.Empty;
            _pairedCredentialsKey = string.Empty;
            CredentialImportStatus = IsRussian
                ? "Введите исходный Base64-ключ файла логинов. Это не пароль Steam, облака или .maFile."
                : "Enter the original Base64 credentials key, not your Steam, cloud or .maFile password.";
            IsCredentialImportVisible = true;
            CredentialsStatus = IsRussian ? "Файл скачан. Ожидается проверка ключа." : "Downloaded. Waiting for key verification.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or HttpRequestException or InvalidOperationException)
        {
            CleanupDownloadedCredentials();
            CredentialsStatus = IsRussian ? "Не удалось загрузить логины: " + ex.Message : "Could not download credentials: " + ex.Message;
        }
        finally
        {
            IsCredentialDownloadBusy = false;
            if (!IsCredentialImportVisible) _resumeAutoLoginAfterCredentialImport = false;
        }
    }

    private void CleanupDownloadedCredentials()
    {
        if (_downloadedCredentialPath is null) return;
        try { File.Delete(_downloadedCredentialPath); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* ciphertext only; no local vault was overwritten */ }
        _downloadedCredentialPath = null;
    }
}
