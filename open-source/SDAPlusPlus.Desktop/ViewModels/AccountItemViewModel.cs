using CommunityToolkit.Mvvm.ComponentModel;
using Avalonia.Media;
using Avalonia.Threading;
using SDAPlusPlus.Core.Vault;
using SDAPlusPlus.Desktop.Services;

namespace SDAPlusPlus.Desktop.ViewModels;

public partial class AccountItemViewModel(VaultAccount account, bool isRussian) : ViewModelBase
{
    private bool _isRussian = isRussian;
    public VaultAccount Account { get; } = account;
    public string Name => Account.Name;
    public string SteamId => Account.SteamId;
    public string Initial => string.IsNullOrWhiteSpace(Name) ? "?" : Name[..1].ToUpperInvariant();
    public bool HasAvatar => Avatar is not null;
    public bool ShowAvatarFallback => !HasAvatar;
    public string SessionLabel => !string.IsNullOrWhiteSpace(Account.GuardAccount.Session?.AccessToken)
        ? (_isRussian ? "Сессия активна" : "Session live")
        : (_isRussian ? "Только коды Steam Guard" : "Guard codes only");
    public string FavoriteActionLabel => IsFavorite
        ? (_isRussian ? "Убрать из избранного" : "Remove favorite")
        : (_isRussian ? "В избранное" : "Favorite");

    [ObservableProperty]
    private string _code = "-----";

    [ObservableProperty]
    private int _secondsRemaining = 30;

    [ObservableProperty]
    private bool _isFavorite = account.IsFavorite;

    [ObservableProperty]
    private IImage? _avatar;

    public async Task LoadAvatarAsync()
    {
        IImage? image = await SteamAvatarLoader.LoadBySteamIdAsync(SteamId);
        await Dispatcher.UIThread.InvokeAsync(() => Avatar = image);
    }

    public void RefreshCode(long unixTime)
    {
        Code = Account.GuardAccount.GenerateSteamGuardCodeForTime(unixTime) ?? "-----";
        SecondsRemaining = 30 - (int)(unixTime % 30);
    }

    public void SetLanguage(bool isRussian)
    {
        _isRussian = isRussian;
        OnPropertyChanged(nameof(SessionLabel));
        OnPropertyChanged(nameof(FavoriteActionLabel));
    }

    public void RefreshSessionState() => OnPropertyChanged(nameof(SessionLabel));

    public void SetFavorite(bool favorite)
    {
        IsFavorite = favorite;
        OnPropertyChanged(nameof(FavoriteActionLabel));
    }

    partial void OnAvatarChanged(IImage? value)
    {
        OnPropertyChanged(nameof(HasAvatar));
        OnPropertyChanged(nameof(ShowAvatarFallback));
    }
}
