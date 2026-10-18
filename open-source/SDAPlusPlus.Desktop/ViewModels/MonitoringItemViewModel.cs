using SDAPlusPlus.Core.Monitoring;
using SDAPlusPlus.Core.Preferences;

namespace SDAPlusPlus.Desktop.ViewModels;

public sealed class MonitoringItemViewModel(AccountMonitoringSnapshot snapshot, AppLanguage language)
{
    private bool IsRussian => language == AppLanguage.Russian;

    public string AccountName => snapshot.Account.Name;
    public string SteamId => snapshot.Account.SteamId;
    public string Session => snapshot.SessionHealth switch
    {
        MonitoredSessionHealth.Active => IsRussian ? "Активна" : "Active",
        MonitoredSessionHealth.AccessExpired => IsRussian ? "Access истёк" : "Access expired",
        MonitoredSessionHealth.RefreshExpired => IsRussian ? "Refresh истёк" : "Refresh expired",
        _ => IsRussian ? "Нет web-сессии" : "No web session"
    };
    public string Vac => snapshot.VacStatus == "Clear" ? (IsRussian ? "Чисто" : "Clear") : snapshot.VacStatus;
    public string Level => snapshot.SteamLevel?.ToString() ?? (IsRussian ? "Скрыт" : "Private");
    public string Games => snapshot.GameCount?.ToString() ?? (IsRussian ? "Скрыты" : "Private");
    public string Inventory => snapshot.InventoryState switch
    {
        MonitoredInventoryState.Available => snapshot.TradableItemCount?.ToString() ?? "0",
        MonitoredInventoryState.Private => IsRussian ? "Приватный" : "Private",
        MonitoredInventoryState.RateLimited => IsRussian ? "Лимит Steam" : "Rate limited",
        _ => IsRussian ? "Недоступен" : "Unavailable"
    };
    public string Detail => snapshot.Error ?? (snapshot.IsCached
        ? (IsRussian ? "Из кэша" : "Cached")
        : snapshot.LoadedAtUtc.ToLocalTime().ToString("g"));
}
