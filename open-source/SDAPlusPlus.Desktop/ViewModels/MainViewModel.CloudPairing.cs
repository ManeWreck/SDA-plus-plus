using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SDAPlusPlus.Core.Cloud;
using SDAPlusPlus.Core.Devices;
using System.IO;
using System.Security.Cryptography;

namespace SDAPlusPlus.Desktop.ViewModels;

public partial class MainViewModel
{
    private static readonly HttpClient CloudPairingHttp = new() { Timeout = TimeSpan.FromSeconds(8) };
    private CancellationTokenSource? _cloudPairingCancellation;
    private CloudPairingProfile? _receivedCloudProfile;
    private string _pairedCredentialsKey = string.Empty;
    private string _pairedCredentialsPath = string.Empty;
    private string _pairedCloudEndpoint = string.Empty;
    private string _pairedCloudUsername = string.Empty;
    [ObservableProperty] private bool _isCloudPairingVisible;
    [ObservableProperty] private bool _isCloudPairingBusy;
    [ObservableProperty] private bool _hasReceivedCloudProfile;
    [ObservableProperty] private IImage? _cloudPairingQr;
    [ObservableProperty] private string _cloudPairingCode = string.Empty;
    [ObservableProperty] private string _cloudPairingStatus = string.Empty;
    [ObservableProperty] private string _cloudPairingRelay = PairingRelayService.ManagedEndpoint;
    [ObservableProperty] private string _cloudPairingExportKey = string.Empty;
    public bool HasSelectedAccount => SelectedAccount is not null;
    public string CloudPairingTitle => IsRussian ? "Подключить SDA++ Mobile" : "Connect SDA++ Mobile";
    public string PhoneToPcLabel => IsRussian ? "Телефон → этот ПК" : "Phone → this PC";
    public string PcToPhoneLabel => IsRussian ? "Этот ПК → телефон" : "This PC → phone";
    public string CloudPairingHelp => IsRussian
        ? "Выберите направление. В SDA++ Mobile откройте подключение к ПК, отсканируйте QR и введите показанный код. Передаётся зашифрованный профиль WebDAV; QR не содержит пароль. Код действует 2 минуты."
        : "Choose a direction. In SDA++ Mobile open desktop pairing, scan the QR and enter the displayed code. The WebDAV profile is encrypted; the QR contains no password. The code expires in 2 minutes.";
    public string CloudPairingExportHelp => IsRussian
        ? "Для ПК → телефон сначала заполните WebDAV на странице облака, включая пароль. При необходимости укажите прежний ключ логинов ниже — он будет передан внутри зашифрованного профиля."
        : "For PC → phone, first fill in the WebDAV profile on the cloud page, including its password. Optionally enter the existing credentials key below to include it in the encrypted profile.";
    public string OptionalPairingKeyLabel => IsRussian ? "Ключ логинов (необязательно)" : "Credentials key (optional)";
    public string ApplyCloudPairingLabel => IsRussian ? "Применить профиль" : "Apply profile";

    [RelayCommand]
    private void OpenCloudPairing()
    {
        IsInitialSetupVisible = false;
        ShowCloud();
        CloudPairingStatus = string.Empty;
        CloudPairingRelay = string.IsNullOrWhiteSpace(_deviceRegistry.Settings.RelayUrl)
            ? PairingRelayService.ManagedEndpoint : _deviceRegistry.Settings.RelayUrl;
        IsCloudPairingVisible = true;
    }

    [RelayCommand] private Task ReceivePhoneProfileAsync() => StartCloudPairingAsync(false);
    [RelayCommand] private Task SendPhoneProfileAsync() => StartCloudPairingAsync(true);

    private async Task StartCloudPairingAsync(bool send)
    {
        if (IsCloudPairingBusy) return;
        if (_deviceRegistry.Settings.IsConfigured && _deviceRegistry.Settings.Role != "Admin")
        {
            CloudPairingStatus = IsRussian ? "Передача облачного профиля доступна только Admin-устройству." : "Cloud profile pairing requires an Admin device.";
            return;
        }
        _receivedCloudProfile = null;
        HasReceivedCloudProfile = false;
        ClearCloudPairingQr();
        using var cancellation = new CancellationTokenSource();
        _cloudPairingCancellation = cancellation;
        IsCloudPairingBusy = true;
        try
        {
            CloudPairingProfile? profile = null;
            if (send)
            {
                if (!IsWebDavProvider) throw new InvalidOperationException(IsRussian ? "Выберите WebDAV на странице облака." : "Select WebDAV on the cloud page.");
                if (!string.IsNullOrWhiteSpace(CloudPairingExportKey) && Convert.FromBase64String(CloudPairingExportKey.Trim()).Length != 32)
                    throw new InvalidDataException(IsRussian ? "Ключ логинов должен содержать 32 байта в Base64." : "The credentials key must contain 32 Base64-encoded bytes.");
                profile = new(WebDavEndpoint.Trim(), WebDavUsername.Trim(), WebDavPassword,
                    string.IsNullOrWhiteSpace(WebDavRemotePath) ? "SDAppVault" : WebDavRemotePath.Trim(), CloudPairingExportKey.Trim(),
                    WebDavRemotePath, CloudPairingRelay == PairingRelayService.ManagedEndpoint ? "managed" : "personal", CloudPairingRelay);
                profile.Validate();
            }
            EnsureEnrollmentClient();
            if (_deviceRequestSigner is not IDeviceRequestSigner signer)
                throw new InvalidOperationException(IsRussian ? "Не удалось открыть ключ устройства Windows." : "The Windows device identity is unavailable.");
            using var session = new CloudProfilePairingSession(CloudPairingHttp, CloudPairingRelay.Trim(), signer, profile);
            CloudPairingQr = _qrCodeRenderer.Render(session.PairingUri, 320);
            CloudPairingCode = session.VerificationCode;
            CloudPairingExportKey = string.Empty;
            CloudPairingStatus = IsRussian ? "Ожидание телефона… Отсканируйте QR и введите код в Mobile." : "Waiting for the phone… Scan the QR and enter the code in Mobile.";
            _receivedCloudProfile = await session.RunAsync(cancellation.Token);
            HasReceivedCloudProfile = _receivedCloudProfile is not null;
            CloudPairingStatus = _receivedCloudProfile is { } received
                ? (IsRussian ? $"Получен профиль: {received.Url}\nЛогин: {received.Login}\nПапка: {received.RemotePath}\nПримените его, затем загрузите аккаунты из облака."
                    : $"Received profile: {received.Url}\nLogin: {received.Login}\nFolder: {received.RemotePath}\nApply it, then import your accounts from the cloud.")
                : (IsRussian ? "Зашифрованный профиль передан. Проверьте результат на телефоне." : "Encrypted profile delivered. Check the result on your phone.");
        }
        catch (OperationCanceledException)
        { CloudPairingStatus = IsRussian ? "Сеанс завершён или истёк. Можно создать новый QR." : "Session cancelled or expired. You can create a new QR."; }
        catch (Exception ex) when (ex is IOException or HttpRequestException or CryptographicException or ArgumentException or InvalidOperationException or FormatException)
        { CloudPairingStatus = ex.Message; }
        finally
        {
            ClearCloudPairingQr();
            IsCloudPairingBusy = false;
            if (ReferenceEquals(_cloudPairingCancellation, cancellation)) _cloudPairingCancellation = null;
        }
    }

    [RelayCommand]
    private void ApplyCloudPairing()
    {
        if (_receivedCloudProfile is not { } profile) return;
        try
        {
            _preferenceStore.Save(_preferenceStore.Load() with
            {
                CloudProvider = CloudProviderKind.WebDav, WebDavEndpoint = profile.Url,
                WebDavUsername = profile.Login, WebDavRemotePath = profile.RemotePath, InitialSetupCompleted = true
            });
            SelectedCloudProviderIndex = (int)CloudProviderKind.WebDav;
            WebDavEndpoint = profile.Url; WebDavUsername = profile.Login; WebDavRemotePath = profile.RemotePath;
            WebDavPassword = profile.Password; // Session only; never write a password to preferences.
            _pairedCredentialsKey = profile.CredentialsKey;
            _pairedCredentialsPath = profile.CredentialsRemotePath;
            _pairedCloudEndpoint = profile.Url;
            _pairedCloudUsername = profile.Login;
            _initialSetupCompleted = true;
            CloudStatus = IsRussian ? "Профиль получен с телефона. Можно проверить подключение и загрузить аккаунты." : "Profile received from your phone. You can test the connection and import accounts.";
            CloseCloudPairing();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { CloudPairingStatus = ex.Message; }
    }

    [RelayCommand]
    private void CloseCloudPairing()
    {
        _cloudPairingCancellation?.Cancel();
        ClearCloudPairingQr();
        _receivedCloudProfile = null;
        HasReceivedCloudProfile = false;
        CloudPairingExportKey = string.Empty;
        IsCloudPairingVisible = false;
    }
    private void ClearCloudPairingQr()
    {
        if (CloudPairingQr is IDisposable image) image.Dispose();
        CloudPairingQr = null;
        CloudPairingCode = string.Empty;
    }
}
