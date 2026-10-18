using CommunityToolkit.Mvvm.ComponentModel;

namespace SDAPlusPlus.Desktop.ViewModels;

public partial class MainViewModel
{
    [ObservableProperty] private string _recipientIdentityCode = string.Empty;
    [ObservableProperty] private string _pairingRecipientCode = string.Empty;
}
