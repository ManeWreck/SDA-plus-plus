using CommunityToolkit.Mvvm.Input;
using SDAPlusPlus.Core.Navigation;

namespace SDAPlusPlus.Desktop.ViewModels;

public partial class MainViewModel
{
    private readonly NavigationHistory<string> _navigation = new("dashboard");
    public bool CanNavigateBack => _navigation.CanGoBack && !IsNavigationBlocked;
    public bool CanNavigateForward => _navigation.CanGoForward && !IsNavigationBlocked;
    public bool IsNavigationBlocked => IsCloudPairingVisible || IsLoginPanelVisible || IsAutoLoginAllPanelVisible ||
        IsCredentialImportVisible || IsCredentialSetupVisible || IsPlainCredentialImportWarningVisible ||
        IsCredentialVaultDeleteConfirmationVisible || IsProtectionConfirmationVisible ||
        IsSteamSessionsPanelVisible || IsEndSessionsPanelVisible || IsAccountImportVisible ||
        IsPairingVisible || IsJoinPairingVisible || IsCredentialDownloadBusy || IsInitialSetupVisible || IsCredentialCloudAuthVisible;

    [RelayCommand]
    private void NavigateBack() { if (CanNavigateBack) RestorePage(_navigation.Back()); }
    [RelayCommand]
    private void NavigateForward() { if (CanNavigateForward) RestorePage(_navigation.Forward()); }

    private void RestorePage(string page)
    {
        SetPage(page == "settings", page == "cloud", page == "qr", page == "confirmations",
            page == "monitoring", page == "credentials", page == "encryption", page == "devices");
        if (page == "credentials") RefreshStoredCredentials();
        if (page == "encryption") RefreshVaultProtectionStatus();
        if (page == "devices") RefreshDeviceRegistry();
    }
}
