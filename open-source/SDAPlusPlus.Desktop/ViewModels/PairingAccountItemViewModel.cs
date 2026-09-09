using CommunityToolkit.Mvvm.ComponentModel;
using SDAPlusPlus.Core.Vault;

namespace SDAPlusPlus.Desktop.ViewModels;

public partial class PairingAccountItemViewModel : ViewModelBase
{
    public PairingAccountItemViewModel(VaultAccount account) => Account = account;

    public VaultAccount Account { get; }
    public string Name => Account.Name;
    public string SteamId => Account.SteamId;

    [ObservableProperty]
    private bool _isSelected = true;
}
