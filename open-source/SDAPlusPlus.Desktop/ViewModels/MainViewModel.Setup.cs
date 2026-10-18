using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SDAPlusPlus.Core.Vault;
using System.IO;

namespace SDAPlusPlus.Desktop.ViewModels;

public partial class MainViewModel
{
    private bool _initialSetupCompleted;
    [ObservableProperty] private bool _isInitialSetupVisible;
    [ObservableProperty] private string _initialSetupStatus = string.Empty;
    [ObservableProperty] private bool _isLocalSetupBusy;
    public string InitialSetupTitle => IsRussian ? "Добро пожаловать в SDA++" : "Welcome to SDA++";
    public string InitialSetupLabel => IsRussian ? "Подключение аккаунтов" : "Account setup";
    public string InitialSetupCloudLabel => IsRussian ? "Подключить облачное хранилище" : "Connect a cloud vault";
    public string InitialSetupLocalLabel => IsRussian ? "Импортировать папку SDA / SDA++ с этого ПК" : "Import an SDA / SDA++ folder from this PC";
    public string InitialSetupFreshLabel => IsRussian ? "Начать с QR-входа" : "Start with QR sign-in";
    public string InitialSetupDevicesLabel => IsRussian ? "Подключение доверенного устройства" : "Connect a trusted device";

    public void ShowInitialSetupIfNeeded()
    {
        if (!_initialSetupCompleted && !File.Exists(Path.Combine(_maFilesDirectory, "manifest.json")) &&
            (!Directory.Exists(_maFilesDirectory) || !Directory.EnumerateFiles(_maFilesDirectory, "*.maFile").Any()))
            IsInitialSetupVisible = true;
    }

    [RelayCommand] private void OpenInitialSetup() { InitialSetupStatus = string.Empty; IsInitialSetupVisible = true; }
    [RelayCommand] private void CloseInitialSetup() { if (!IsLocalSetupBusy) IsInitialSetupVisible = false; }
    private void CompleteInitialSetup()
    {
        _initialSetupCompleted = true;
        // Do not persist unrelated unsaved fields from the Settings screen.
        _preferenceStore.Save(_preferenceStore.Load() with { InitialSetupCompleted = true });
        IsInitialSetupVisible = false;
    }
    [RelayCommand] private void InitialSetupCloud() { CompleteInitialSetup(); ShowCloud(); }
    [RelayCommand] private void InitialSetupFresh() { CompleteInitialSetup(); ShowQr(); }
    [RelayCommand] private async Task InitialSetupDevicesAsync() { CompleteInitialSetup(); await ShowDevicesAsync(); }

    [RelayCommand]
    private async Task ImportLocalSetupAsync()
    {
        if (IsLocalSetupBusy) return;
        if (File.Exists(Path.Combine(_maFilesDirectory, "manifest.json")) ||
            Directory.Exists(_maFilesDirectory) && Directory.EnumerateFiles(_maFilesDirectory, "*.maFile").Any())
        {
            InitialSetupStatus = IsRussian ? "Хранилище уже существует. Добавляйте отдельные .maFile через импорт аккаунта — папка не будет заменена."
                : "A vault already exists. Import individual .maFile accounts instead; the folder will not be replaced.";
            return;
        }
        string? source = await _accountFilePicker.PickVaultFolderAsync();
        if (source is null) return;
        IsLocalSetupBusy = true;
        try
        {
            var prepared = await Task.Run(() => LocalVaultFolderImport.Prepare(source));
            if (!prepared.Success || prepared.Package is null) { InitialSetupStatus = prepared.Message; return; }
            var result = _vaultImportService.Commit(prepared.Package, _maFilesDirectory);
            if (!result.Success) { InitialSetupStatus = result.Message; return; }
            ApplyVaultResult(_vaultReader.Load(_maFilesDirectory));
            CompleteInitialSetup();
            ShowDashboard();
            StatusMessage = IsRussian ? $"Импортировано аккаунтов: {result.AccountCount}. Исходная папка сохранена."
                : $"Imported {result.AccountCount} accounts. The source folder is unchanged.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { InitialSetupStatus = ex.Message; }
        finally { IsLocalSetupBusy = false; }
    }
}
