using CommunityToolkit.Mvvm.ComponentModel;
using SDAPlusPlus.Core.Credentials;

namespace SDAPlusPlus.Desktop.ViewModels;

public partial class AccountCredentialRowViewModel : ViewModelBase
{
    private bool _isRussian;

    public AccountCredentialRowViewModel(AccountItemViewModel account, StoredCredential? credential, bool isRussian)
    {
        Account = account;
        _isRussian = isRussian;
        Username = credential?.Username ?? account.Name;
        HasSavedPassword = credential is not null;
    }

    public AccountItemViewModel Account { get; }
    public string AccountName => Account.Name;
    public string SteamId => Account.SteamId;
    public string Username { get; }
    public string Status => HasSavedPassword
        ? (_isRussian ? "Пароль сохранён" : "Password saved")
        : (_isRussian ? "Требуется пароль" : "Password required");
    public string SaveLabel => _isRussian ? "Сохранить" : "Save";

    [ObservableProperty]
    private string _password = string.Empty;

    [ObservableProperty]
    private bool _hasSavedPassword;

    public void ClearPassword() => Password = string.Empty;

    public void SetSaved(bool saved)
    {
        HasSavedPassword = saved;
        OnPropertyChanged(nameof(Status));
    }

    public void SetLanguage(bool isRussian)
    {
        _isRussian = isRussian;
        OnPropertyChanged(nameof(Status));
        OnPropertyChanged(nameof(SaveLabel));
    }
}
