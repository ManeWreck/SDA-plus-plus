using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SDAPlusPlus.Core.Devices;

namespace SDAPlusPlus.Desktop.ViewModels;

public partial class MainViewModel
{
    public ObservableCollection<string> IncomingCommandRows { get; } = [];
    [ObservableProperty] private string _incomingCommandsStatus = string.Empty;
    public string IncomingCommandsLabel => IsRussian ? "Команды для этого ПК" : "Commands for this PC";
    public string IncomingCommandsHelp => IsRussian
        ? "Только просмотр очереди. Исполнение и автоматическая отправка подтверждений пока не включены."
        : "Queue preview only. Execution and automatic acknowledgments are not enabled yet.";

    [RelayCommand]
    private async Task RefreshIncomingCommandsAsync()
    {
        IncomingCommandRows.Clear();
        if (_deviceRegistryClient is null)
        {
            IncomingCommandsStatus = IsRussian ? "Ключ устройства недоступен." : "Device key unavailable.";
            return;
        }
        var client = _deviceRegistryClient;
        var settings = _deviceRegistry.Settings;
        IncomingCommandsStatus = IsRussian ? "Получение очереди..." : "Loading queue...";
        try
        {
            var result = await client.PendingCommandsAsync(settings);
            if (!ReferenceEquals(client, _deviceRegistryClient) || !ReferenceEquals(settings, _deviceRegistry.Settings))
            {
                IncomingCommandsStatus = IsRussian ? "Реестр изменился. Повторите запрос." : "Registry changed. Refresh again.";
                return;
            }
            if (!result.Success)
            {
                IncomingCommandsStatus = IsRussian ? "Не удалось получить очередь. Проверьте подключение и поддержку команд сервером." : "Could not load queue. Check connectivity and server command support.";
                return;
            }
            foreach (var command in result.Commands)
            {
                bool expired = command.ExpiresAt <= DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                string state = expired ? (IsRussian ? "Срок истёк" : "Expired") : (IsRussian ? "Ожидает исполнения" : "Pending");
                IncomingCommandRows.Add($"{command.Type} | {state} | {command.CommandId}");
            }
            IncomingCommandsStatus = IsRussian ? $"Команд: {result.Commands.Count}. Это не подтверждение исполнения." : $"Commands: {result.Commands.Count}. This does not confirm execution.";
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or Newtonsoft.Json.JsonException or InvalidOperationException)
        {
            IncomingCommandsStatus = IsRussian ? "Очередь временно недоступна." : "Queue temporarily unavailable.";
        }
    }
}
