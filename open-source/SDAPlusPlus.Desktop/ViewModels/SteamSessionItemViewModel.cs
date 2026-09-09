using SDAPlusPlus.Core.Sessions;

namespace SDAPlusPlus.Desktop.ViewModels;

public sealed class SteamSessionItemViewModel(SteamActiveSession session, string accountName, bool isRussian)
{
    public ulong TokenId => session.TokenId;
    public string DeviceName => session.DeviceName;
    public string Platform => session.Platform;
    public string IpAddress => string.IsNullOrWhiteSpace(session.IpAddress) ? "—" : session.IpAddress;
    public string Location => string.IsNullOrWhiteSpace(session.Location) ? "—" : session.Location;
    public bool IsCurrent => session.IsCurrent;
    public string AccountLabel => string.Format(
        isRussian ? "Аккаунт: {0}" : "Account: {0}",
        accountName);
    public string CurrentLabel => session.IsCurrent
        ? (isRussian ? "Текущая сессия SDA++" : "Current SDA++ session")
        : string.Empty;
    public string LastSeenLabel => session.LastSeen is null
        ? (isRussian ? "Активность неизвестна" : "Activity unknown")
        : string.Format(
            isRussian ? "Последняя активность: {0:g}" : "Last active: {0:g}",
            session.LastSeen.Value.ToLocalTime());
}
