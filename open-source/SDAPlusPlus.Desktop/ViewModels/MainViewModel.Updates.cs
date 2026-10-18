using System.Diagnostics;
using System.Reflection;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SDAPlusPlus.Core.Updates;

namespace SDAPlusPlus.Desktop.ViewModels;

public partial class MainViewModel
{
    [ObservableProperty] private bool _checkUpdatesOnStartup = true;
    [ObservableProperty] private string _updateStatus = string.Empty;
    [ObservableProperty] private bool _hasAvailableUpdate;
    private AvailableUpdate? _availableUpdate;
    private DateTimeOffset _nextUpdateCheck;

    public string CurrentApplicationVersion => typeof(MainViewModel).Assembly
        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0] ?? "1.7.1-security-experimental";
    public string UpdatesTitle => IsRussian ? "Обновления приложения" : "Application updates";
    public string StartupUpdatesLabel => IsRussian ? "Проверять GitHub при запуске (сохраните настройку кнопкой «Сохранить»)" : "Check GitHub on startup (use Save to keep this setting)";
    public string CheckUpdatesLabel => IsRussian ? "Проверить обновления" : "Check for updates";
    public string OpenUpdateLabel => IsRussian ? "Открыть новую версию" : "Open new release";
    public string UpdateChannelLabel => $"{CurrentApplicationVersion} | " + (CurrentApplicationVersion.Contains('-') ? "Experimental" : "Stable");
    public string UpdateHelp => IsRussian
        ? "Проверка автоматическая, установка ручная. Перед обновлением сохраните резервную копию хранилища. Данные аккаунтов не отправляются в GitHub."
        : "Automatic checks, manual installation. Back up your vault before updating. No account data is sent to GitHub.";

    [RelayCommand]
    private async Task CheckForUpdatesAsync()
    {
        if (DateTimeOffset.UtcNow < _nextUpdateCheck) return;
        _nextUpdateCheck = DateTimeOffset.UtcNow.AddMinutes(5);
        UpdateStatus = IsRussian ? "Проверяем GitHub..." : "Checking GitHub...";
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
            _availableUpdate = await new GitHubUpdateChecker(client).CheckAsync(CurrentApplicationVersion, CurrentApplicationVersion.Contains('-'));
            HasAvailableUpdate = _availableUpdate is not null;
            UpdateStatus = _availableUpdate is null
                ? (IsRussian ? "Новых совместимых версий не найдено." : "No newer compatible release found.")
                : (IsRussian ? "Доступно обновление: " : "Update available: ") + _availableUpdate.Version;
            if (HasAvailableUpdate) StatusMessage = UpdateStatus;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException or FormatException or InvalidOperationException)
        {
            UpdateStatus = IsRussian ? "Не удалось проверить GitHub. Повторите через 5 минут; приложение продолжает работать." : "Could not check GitHub. Retry in 5 minutes; the app remains usable.";
        }
    }

    [RelayCommand]
    private void OpenAvailableUpdate()
    {
        if (_availableUpdate is null) return;
        try { Process.Start(new ProcessStartInfo(_availableUpdate.ReleaseUrl) { UseShellExecute = true }); }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        { UpdateStatus = IsRussian ? "Не удалось открыть браузер." : "Could not open the browser."; }
    }
}
