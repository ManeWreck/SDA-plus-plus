using System.Collections.ObjectModel;
using System.Security.Cryptography;
using System.Text;
using Avalonia.Threading;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SDAPlusPlus.Core.Cloud;
using SDAPlusPlus.Core.Confirmations;
using SDAPlusPlus.Core.Credentials;
using SDAPlusPlus.Core.Devices;
using SDAPlusPlus.Core.Hotkeys;
using SDAPlusPlus.Core.Localization;
using SDAPlusPlus.Core.Monitoring;
using SDAPlusPlus.Core.Preferences;
using SDAPlusPlus.Core.Qr;
using SDAPlusPlus.Core.Sessions;
using SDAPlusPlus.Core.Vault;
using SDAPlusPlus.Desktop.Services;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace SDAPlusPlus.Desktop.ViewModels;

public partial class MainViewModel : ViewModelBase, IDisposable
{
    private readonly DispatcherTimer _codeTimer;
    private readonly DispatcherTimer _confirmationPollingTimer;
    private readonly Dictionary<string, DateTimeOffset> _automaticConfirmationAttempts = new(StringComparer.Ordinal);
    private bool _confirmationPollingBusy;
    private readonly List<AccountItemViewModel> _allAccounts = [];
    private readonly ILegacyVaultReader _vaultReader;
    private readonly IPreferenceStore _preferenceStore;
    private readonly IClipboardService _clipboardService;
    private readonly ICloudVaultProvider _cloudProvider;
    private readonly IVaultImportService _vaultImportService;
    private readonly IScreenQrScanner _screenQrScanner;
    private readonly ISteamQrApprovalService _qrApprovalService;
    private readonly IConfirmationService _confirmationService;
    private readonly IGlobalHotkeyService _globalHotkeyService;
    private readonly ISteamSessionLoginService _sessionLoginService;
    private readonly IVaultAccountWriter _vaultAccountWriter;
    private readonly ISteamSessionTerminationService _sessionTerminationService;
    private readonly IVaultAccountImportService _accountImportService;
    private readonly IAccountFilePicker _accountFilePicker;
    private readonly IHotkeyOverlayService _hotkeyOverlayService;
    private readonly IAccountMonitoringService _accountMonitoringService;
    private readonly IStoredCredentialVault _credentialVault;
    private readonly IVaultEncryptionService _vaultEncryptionService;
    private readonly IDeviceRegistryStore _deviceRegistryStore;
    private readonly IPairingRelayService _pairingRelayService;
    private IDeviceRegistryClient? _deviceRegistryClient;
    private IDisposable? _deviceRequestSigner;
    private readonly AdminPinService _adminPinService;
    private readonly IQrCodeRenderer _qrCodeRenderer;
    private DeviceRegistryLoadResult _deviceRegistry;
    private CloudPullPackage? _pendingCloudPackage;
    private readonly string _maFilesDirectory;
    private DeviceActionApproval? _deviceActionApproval;
    private CancellationTokenSource? _loginCancellation;
    private TaskCompletionSource<string?>? _emailCodeCompletion;
    private CancellationTokenSource? _credentialSetupCountdown;
    private CancellationTokenSource? _confirmationRefreshCancellation;

    public MainViewModel()
        : this(
            new LegacyVaultReader(),
            new JsonPreferenceStore(JsonPreferenceStore.GetDefaultPath()),
            new AvaloniaClipboardService(),
            new CloudVaultProviderRouter(new HttpClient { Timeout = TimeSpan.FromSeconds(15) }, ResolveMaFilesDirectory()),
            new VaultImportService(new LegacyVaultReader()),
            new WindowsScreenQrScanner(),
            new SteamQrApprovalService(),
            new SteamConfirmationService(),
            new WindowsGlobalHotkeyService(),
            new SteamSessionLoginService(),
            new LegacyVaultAccountWriter(new LegacyVaultReader()),
            new SteamSessionTerminationService(),
            new LegacyVaultAccountImportService(new LegacyVaultReader()),
            new AvaloniaAccountFilePicker(),
            new AvaloniaHotkeyOverlayService(),
            ResolveMaFilesDirectory())
    {
    }

    internal MainViewModel(
        ILegacyVaultReader vaultReader,
        IPreferenceStore preferenceStore,
        IClipboardService clipboardService,
        ICloudVaultProvider cloudProvider,
        IVaultImportService vaultImportService,
        IScreenQrScanner screenQrScanner,
        ISteamQrApprovalService qrApprovalService,
        IConfirmationService confirmationService,
        IGlobalHotkeyService globalHotkeyService,
        ISteamSessionLoginService sessionLoginService,
        IVaultAccountWriter vaultAccountWriter,
        ISteamSessionTerminationService sessionTerminationService,
        IVaultAccountImportService accountImportService,
        IAccountFilePicker accountFilePicker,
        IHotkeyOverlayService hotkeyOverlayService,
        string maFilesDirectory,
        IAccountMonitoringService? accountMonitoringService = null,
        IStoredCredentialVault? credentialVault = null,
        IVaultEncryptionService? vaultEncryptionService = null,
        IDeviceRegistryStore? deviceRegistryStore = null,
        IPairingRelayService? pairingRelayService = null,
        IDeviceRegistryClient? deviceRegistryClient = null,
        IQrCodeRenderer? qrCodeRenderer = null)
    {
        _vaultReader = vaultReader;
        _preferenceStore = preferenceStore;
        _clipboardService = clipboardService;
        _cloudProvider = cloudProvider;
        _vaultImportService = vaultImportService;
        _screenQrScanner = screenQrScanner;
        _qrApprovalService = qrApprovalService;
        _confirmationService = confirmationService;
        _globalHotkeyService = globalHotkeyService;
        _sessionLoginService = sessionLoginService;
        _vaultAccountWriter = vaultAccountWriter;
        _sessionTerminationService = sessionTerminationService;
        _accountImportService = accountImportService;
        _accountFilePicker = accountFilePicker;
        _hotkeyOverlayService = hotkeyOverlayService;
        _accountMonitoringService = accountMonitoringService ?? new SteamCommunityMonitoringService();
        _credentialVault = credentialVault ?? new LegacyStoredCredentialVault(maFilesDirectory);
        _vaultEncryptionService = vaultEncryptionService ?? new LegacyVaultEncryptionService(vaultReader);
        _deviceRegistryStore = deviceRegistryStore ?? new WindowsDeviceRegistryStore(maFilesDirectory);
        _pairingRelayService = pairingRelayService ?? new PairingRelayService(new HttpClient { Timeout = TimeSpan.FromSeconds(10) });
        _deviceRegistry = _deviceRegistryStore.Load();
        _adminPinService = new AdminPinService(_deviceRegistryStore);
        _qrCodeRenderer = qrCodeRenderer ?? new AvaloniaQrCodeRenderer();
        if (deviceRegistryClient is not null)
        {
            _deviceRegistryClient = deviceRegistryClient;
        }
        else if (_deviceRegistry.State == DeviceRegistryLoadState.Ready && _deviceRegistry.Settings.IsConfigured && OperatingSystem.IsWindows())
        {
            try
            {
                WindowsCngDeviceRequestSigner signer = new();
                _deviceRequestSigner = signer;
                _deviceRegistryClient = new DeviceRegistryClient(new HttpClient { Timeout = TimeSpan.FromSeconds(15) }, signer, _deviceRegistryStore.Save);
            }
            catch (CryptographicException)
            {
                _deviceRegistryClient = null;
            }
        }
        _maFilesDirectory = maFilesDirectory;
        SourcePath = maFilesDirectory;

        AppPreferences preferences = _preferenceStore.Load();
        _initialSetupCompleted = preferences.InitialSetupCompleted;
        CheckUpdatesOnStartup = preferences.CheckUpdatesOnStartup;
        _selectedLanguageIndex = (int)preferences.Language;
        _selectedThemeIndex = (int)preferences.Theme;
        _accountSearchEnabled = preferences.AccountSearchEnabled;
        _favoritesFirst = preferences.FavoritesFirst;
        _periodicConfirmationChecking = preferences.PeriodicConfirmationChecking;
        _confirmationCheckIntervalSeconds = preferences.ConfirmationCheckIntervalSeconds;
        _checkAllAccountsForConfirmations = preferences.CheckAllAccountsForConfirmations;
        _autoConfirmMarketTransactions = preferences.AutoConfirmMarketTransactions;
        _autoConfirmGiftTrades = preferences.AutoConfirmGiftTrades;
        _selectedQrCaptureModeIndex = (int)preferences.QrCaptureMode;
        _qrCursorAreaSize = preferences.QrCursorAreaSize;
        _globalHotkeysEnabled = preferences.GlobalHotkeysEnabled;
        _qrHotkeysEnabled = preferences.QrHotkeysEnabled;
        _qrToggleHotkey = preferences.QrToggleHotkey;
        _qrScanHotkey = preferences.QrScanHotkey;
        _previousAccountHotkey = preferences.PreviousAccountHotkey;
        _nextAccountHotkey = preferences.NextAccountHotkey;
        _autoLoginAllHotkey = preferences.AutoLoginAllHotkey;
        _confirmationsHotkey = preferences.ConfirmationsHotkey;
        _webDavEndpoint = preferences.WebDavEndpoint;
        _webDavUsername = preferences.WebDavUsername;
        _webDavRemotePath = preferences.WebDavRemotePath;
        _selectedCloudProviderIndex = (int)preferences.CloudProvider;
        _s3Endpoint = preferences.S3Endpoint;
        _s3Bucket = preferences.S3Bucket;
        _s3AccessKey = preferences.S3AccessKey;
        _s3RemotePath = preferences.S3RemotePath;
        _dropboxRemotePath = preferences.DropboxRemotePath;
        _oneDriveClientId = preferences.OneDriveClientId;
        _oneDriveRemotePath = preferences.OneDriveRemotePath;
        _googleDriveClientId = preferences.GoogleDriveClientId;
        _googleDriveRemotePath = preferences.GoogleDriveRemotePath;
        _globalHotkeyService.ScanQrRequested += OnGlobalScanQrRequested;
        _globalHotkeyService.ConfirmationsRequested += OnGlobalConfirmationsRequested;
        _globalHotkeyService.ToggleQrRequested += OnGlobalToggleQrRequested;
        _globalHotkeyService.PreviousAccountRequested += OnGlobalPreviousAccountRequested;
        _globalHotkeyService.NextAccountRequested += OnGlobalNextAccountRequested;
        _globalHotkeyService.AutoLoginAllRequested += OnGlobalAutoLoginAllRequested;
        ConfigureHotkeys();
        SetQrState(QrUiState.Ready);
        ApplyVaultResult(_vaultReader.Load(_maFilesDirectory));

        _codeTimer = new DispatcherTimer(
            TimeSpan.FromMilliseconds(250),
            DispatcherPriority.Background,
            (_, _) => RefreshCodes());
        _codeTimer.Start();
        _confirmationPollingTimer = new DispatcherTimer(
            TimeSpan.FromSeconds(Math.Clamp(ConfirmationCheckIntervalSeconds, 5, 3600)),
            DispatcherPriority.Background,
            async (_, _) => await PollConfirmationsAsync());
        UpdateConfirmationPollingTimer();
        RefreshCodes();
    }

    public ObservableCollection<AccountItemViewModel> Accounts { get; } = [];
    public ObservableCollection<ConfirmationItemViewModel> Confirmations { get; } = [];
    public ObservableCollection<MonitoringItemViewModel> MonitoringAccounts { get; } = [];
    public ObservableCollection<CredentialItemViewModel> StoredCredentials { get; } = [];
    public ObservableCollection<AccountCredentialRowViewModel> CredentialAccounts { get; } = [];
    public ObservableCollection<RegistryDeviceItemViewModel> RegistryDevices { get; } = [];
    public ObservableCollection<RegistryAuditItemViewModel> RegistryAuditEvents { get; } = [];
    public ObservableCollection<PairingAccountItemViewModel> PairingAccounts { get; } = [];
    public ObservableCollection<SteamSessionItemViewModel> ActiveSteamSessions { get; } = [];
    public string SourcePath { get; }
    public UiStrings Text => UiStrings.For((AppLanguage)SelectedLanguageIndex);
    public string SelectedAccountName => SelectedAccount?.Name ?? Text.NoAccountSelected;
    public bool IsDashboardVisible => !IsSettingsVisible && !IsCloudVisible && !IsQrVisible && !IsConfirmationsVisible && !IsMonitoringVisible && !IsCredentialsVisible && !IsEncryptionVisible && !IsDevicesVisible;
    public bool IsRussian => SelectedLanguageIndex == (int)AppLanguage.Russian;
    public string EnrollmentDirectory => _maFilesDirectory;
    public string CreateAuthenticatorLabel => IsRussian ? "Создать .maFile" : "Create .maFile";
    public string SteamSessionsTitle => IsRussian ? "Активные Steam-сессии" : "Active Steam sessions";
    public string SteamSessionsSubtitle => IsRussian
        ? "Устройства, IP-адреса и последняя активность выбранного аккаунта."
        : "Devices, IP addresses, and recent activity for the selected account.";
    public string RefreshSteamSessionsLabel => IsRussian ? "Обновить" : "Refresh";
    public string CloseLabel => IsRussian ? "Закрыть" : "Close";
    public string ManageSteamSessionsLabel => IsRussian ? "Steam-сессии" : "Steam sessions";
    public string TerminateAllSteamSessionsLabel => IsRussian
        ? "Завершить все Steam-сессии этого аккаунта"
        : "Terminate all Steam sessions for this account";
    public string EndSessionsActionTitle => IsRussian ? "Завершить все Steam-сессии" : "Terminate all Steam sessions";
    public string ConfirmEndSessionsActionLabel => IsRussian ? "Подтвердить завершение" : "Confirm termination";
    public string ConfirmationAutomationTitle => IsRussian ? "Автоматизация подтверждений" : "Confirmation automation";
    public string PeriodicConfirmationCheckingLabel => IsRussian
        ? "Периодически проверять новые подтверждения"
        : "Periodically check for new confirmations";
    public string ConfirmationIntervalLabel => IsRussian ? "Интервал проверки, секунд" : "Check interval, seconds";
    public string CheckAllConfirmationsLabel => IsRussian
        ? "Проверять подтверждения у всех аккаунтов"
        : "Check confirmations for all accounts";
    public string AutoConfirmMarketLabel => IsRussian
        ? "Автоподтверждать продажи на торговой площадке"
        : "Auto-confirm Community Market listings";
    public string AutoConfirmGiftTradesLabel => IsRussian
        ? "Автоподтверждать только входящие обмены-подарки"
        : "Auto-confirm incoming gift trades only";
    public string ConfirmationAutomationWarning => IsRussian
        ? "Подарок принимается автоматически только после проверки состава: аккаунт ничего не отдаёт и получает минимум один предмет. Обычные обмены всегда остаются ручными."
        : "A gift is accepted automatically only after its contents prove that the account gives nothing and receives at least one item. Regular trades always remain manual.";
    public string ConfirmationSafetyText => AutoConfirmMarketTransactions || AutoConfirmGiftTrades
        ? (IsRussian
            ? "Автоматизация включена согласно настройкам. Все остальные действия требуют отдельного подтверждения."
            : "Automation is enabled as configured. Every other action still requires separate confirmation.")
        : Text.ConfirmationSafety;
    public string MonitoringNav => IsRussian ? "Мониторинг" : "Monitoring";
    public string MonitoringTitle => IsRussian ? "Мониторинг аккаунтов" : "Account monitoring";
    public string MonitoringSubtitle => IsRussian
        ? "VAC, уровень, игры, web-сессия и доступный для обмена CS2-инвентарь без API key."
        : "VAC, level, games, web session, and tradable CS2 inventory without an API key.";
    public string MonitoringAccountHeader => IsRussian ? "Аккаунт" : "Account";
    public string MonitoringSessionHeader => IsRussian ? "Сессия" : "Session";
    public string MonitoringGamesHeader => IsRussian ? "Игры" : "Games";
    public string MonitoringInventoryHeader => IsRussian ? "Tradable" : "Tradable";
    public string MonitoringRefresh => IsRussian ? "Обновить данные" : "Refresh data";
    public string CredentialsNav => IsRussian ? "Логины" : "Credentials";
    public string CredentialsTitle => IsRussian ? "Логины и автовход" : "Login credentials";
    public string CredentialsSubtitle => IsRussian
        ? "Введите пароль напротив аккаунта. Логин берётся из .maFile, а credentials.secure.json можно отправить в выбранное облако."
        : "Enter a password next to an account. The login comes from its .maFile, and credentials.secure.json can be uploaded to the selected cloud.";
    public string CredentialAccountHeader => IsRussian ? "Аккаунт" : "Account";
    public string CredentialLoginHeader => IsRussian ? "Логин Steam" : "Steam login";
    public string CredentialPasswordHeader => IsRussian ? "Пароль Steam" : "Steam password";
    public string CredentialStateHeader => IsRussian ? "Состояние" : "Status";
    public string SyncCredentialsLabel => IsRussian ? "Отправить зашифрованные логины в облако" : "Upload encrypted credentials to cloud";
    public string UpgradeCredentialsLabel => IsRussian ? "Перевести логины на AES-GCM (v3)" : "Upgrade credentials to AES-GCM (v3)";

    public void UpgradeCredentialsEncryption()
    {
        CredentialVaultWriteResult result = _credentialVault.UpgradeEncryption();
        CredentialsStatus = result.IsSuccess
            ? result.BackupPath is not null
                ? (IsRussian ? $"Формат v3 проверен и сохранён. Резервная копия: {result.BackupPath}. Облако не изменено." : $"Verified v3 saved. Backup: {result.BackupPath}. Cloud unchanged.")
                : (IsRussian ? "Файл уже использует v3. Облако не изменено." : "The file already uses v3. Cloud unchanged.")
            : (IsRussian ? "Переход не выполнен. Нужны читаемый зашифрованный файл, исходный 32-байтовый ключ и доступ к диску. Исходный файл не заменён." : result.Detail ?? "Migration failed; original file not replaced.");
        RefreshStoredCredentials(preserveStatus: true);
    }
    public string AutoLoginAllLabel => IsRussian ? "Автовход для всех" : "Auto login all";
    public string ConfigureCredentialsLabel => IsRussian ? "Настроить логины" : "Set up credentials";
    public string AutoLoginAllPasswordExplanation => IsRussian
        ? "Это пароль шифрования папки .maFiles, а не ключ файла credentials.secure.json."
        : "This is the .maFiles vault encryption password, not the credentials.secure.json key.";
    public string StoreLoginLabel => IsRussian ? "Сохранить пароль в зашифрованном vault" : "Store password in encrypted vault";
    public string LoginActionLabel => HasStoredLoginCredential
        ? (IsRussian ? "Войти автоматически" : "Log in automatically")
        : Text.LoginAndSave;
    public string RemoveLabel => IsRussian ? "Удалить" : "Remove";
    public string EncryptionNav => IsRussian ? "Шифрование" : "Encryption";
    public string EncryptionTitle => IsRussian ? "Защита хранилища" : "Vault encryption";
    public string EncryptionSubtitle => IsRussian
        ? "Шифрование всех .maFile совместимым с SDA форматом. Перед изменением всегда создаётся резервная копия."
        : "Encrypt every .maFile using the SDA-compatible format. A backup is always created before any change.";
    public string EncryptionCurrentPasswordLabel => IsRussian ? "Текущий пароль" : "Current password";
    public string EncryptionNewPasswordLabel => IsRussian ? "Новый пароль" : "New password";
    public string EncryptionConfirmPasswordLabel => IsRussian ? "Повторите новый пароль" : "Confirm new password";
    public string EnableEncryptionLabel => IsRussian ? "Включить шифрование" : "Enable encryption";
    public string ChangeEncryptionLabel => IsRussian ? "Сменить пароль" : "Change password";
    public string DisableEncryptionLabel => IsRussian ? "Отключить шифрование" : "Disable encryption";
    public string ApplyProtectionLabel => IsRussian ? "Подтвердить изменение" : "Confirm change";
    public string CreateCredentialsLabel => IsRussian ? "Создать зашифрованный файл" : "Create encrypted vault";
    public string CopyKeyLabel => IsRussian ? "Копировать ключ" : "Copy key";
    public string VerifyKeyLabel => IsRussian ? "Проверить ключ" : "Verify key";
    public string FinalCreateCredentialsLabel => IsRussian ? "Я сохранил ключ — создать" : "I saved the key — create vault";
    public string ImportCredentialsLabel => IsRussian ? "Добавить из зашифрованного файла" : "Merge encrypted vault";
    public string ImportLegacyCredentialsLabel => IsRussian ? "Импорт login:password / JSON" : "Import login:password / JSON";
    public string DeleteCredentialsVaultLabel => IsRussian ? "Удалить файл логинов" : "Delete credentials vault";
    public string AddCredentialLabel => IsRussian ? "Добавить логин" : "Add credentials";
    public string CredentialVaultSectionLabel => IsRussian ? "ЗАЩИЩЁННОЕ ХРАНИЛИЩЕ" : "SECURE VAULT";
    public string CredentialVaultReadyTitle => IsRussian ? "Зашифрованное хранилище готово" : "Encrypted vault ready";
    public string CredentialVaultMissingTitle => IsRussian ? "Хранилище логинов не настроено" : "Credentials vault is not configured";
    public string CredentialVaultSummaryTitle => IsCredentialVaultReady
        ? CredentialVaultReadyTitle
        : IsCredentialVaultMissing
            ? CredentialVaultMissingTitle
            : (IsRussian ? "Хранилище недоступно" : "Credentials vault unavailable");
    public string EmptyCredentialsTitle => IsRussian ? "Сохранённых логинов пока нет" : "No saved credentials yet";
    public string EmptyCredentialsSubtitle => IsRussian
        ? "Нажмите «Добавить логин», выберите аккаунт и сохраните данные в зашифрованном файле."
        : "Select Add credentials, choose an account, and save it to the encrypted vault.";
    public string DeleteCredentialsVaultTitle => IsRussian ? "Удалить зашифрованный файл логинов?" : "Delete the encrypted credentials vault?";
    public string DeleteCredentialsVaultWarning => IsRussian
        ? "Все сохранённые в credentials.secure.json логины и пароли будут удалены с этого компьютера. Аккаунты и .maFiles не удаляются. Это действие нельзя отменить."
        : "All usernames and passwords stored in credentials.secure.json will be removed from this computer. Accounts and .maFiles are not affected. This action cannot be undone.";
    public string CredentialAccountLabel => IsRussian ? "Аккаунт SDA++" : "SDA++ account";
    public string CredentialUsernameLabel => IsRussian ? "Логин Steam" : "Steam login";
    public string CredentialPasswordLabel => IsRussian ? "Пароль Steam" : "Steam password";
    public string SaveCredentialLabel => IsRussian ? "Сохранить в зашифрованный файл" : "Save to encrypted vault";
    public string PlainCredentialImportTitle => IsRussian ? "Импорт login:password или JSON" : "Import login:password or JSON";
    public string PlainCredentialImportWarning => IsRussian
        ? "Поддерживаются списки login:password и JSON с полями login/username и password. Также можно импортировать зашифрованный credentials.secure.json, если рядом находится его исходный cloud.secret.json. Данные объединяются с текущим зашифрованным хранилищем; исходные файлы не изменяются."
        : "Supports login:password lists and JSON containing login/username and password fields. An encrypted credentials.secure.json can also be imported when its original cloud.secret.json is beside it. Data is merged into the current encrypted vault; source files are not modified.";
    public string ImportKeyLabel => IsRussian ? "Ключ расшифрования" : "Decryption key";
    public string DevicesNav => IsRussian ? "Устройства" : "Devices";
    public string DevicesTitle => IsRussian ? "Доверенные устройства" : "Trusted devices";
    public string DevicesSubtitle => IsRussian
        ? "Роли Admin, Trusted и Temporary, разрешённые аккаунты и состояние защищённого relay."
        : "Admin, Trusted, and Temporary roles, granted accounts, and secure relay health.";
    public string DeviceRoleLabel => IsRussian ? "Роль этого устройства" : "This device role";
    public string DeviceAccountsLabel => IsRussian ? "Разрешено аккаунтов" : "Granted accounts";
    public string DeviceExpiresLabel => IsRussian ? "Доступ действует до" : "Access expires";
    public string RelayLabel => IsRussian ? "Cloudflare relay" : "Cloudflare relay";
    public string CheckRelayLabel => IsRussian ? "Проверить Worker" : "Check Worker";
    public string ReloadRegistryLabel => IsRussian ? "Перечитать registry" : "Reload registry";
    public string RefreshDevicesLabel => IsRussian ? "Обновить список" : "Refresh devices";
    public string DeviceNameHeader => IsRussian ? "Устройство" : "Device";
    public string DeviceLastSeenHeader => IsRussian ? "Последняя связь" : "Last seen";
    public string DeviceStateHeader => IsRussian ? "Состояние" : "State";
    public string SecurityActivityLabel => IsRussian ? "Журнал безопасности" : "Security activity";
    public string RefreshActivityLabel => IsRussian ? "Обновить журнал" : "Refresh activity";
    public string ConfigureAdminPinLabel => IsRussian ? "Создать Admin PIN" : "Create Admin PIN";
    public string AdminPinLabel => IsRussian ? "Admin PIN (6–12 цифр)" : "Admin PIN (6–12 digits)";
    public string ConfirmAdminPinLabel => IsRussian ? "Повторите PIN" : "Confirm PIN";
    public string LockDeviceLabel => IsRussian ? "Заблокировать" : "Lock";
    public string RevokeDeviceLabel => IsRussian ? "Отозвать" : "Revoke";
    public string WipeDeviceLabel => IsRussian ? "Очистить данные" : "Wipe data";
    public string CancelLabel => IsRussian ? "Отмена" : "Cancel";
    public string ConfirmDeviceActionLabel => IsRussian ? "Подтвердить действие" : "Confirm action";
    public bool IsAdminPinSetupVisible => !IsAdminPinConfigured;
    public string AddDeviceLabel => IsRussian ? "Добавить устройство" : "Add device";
    public string JoinDeviceLabel => IsRussian ? "Подключить этот ПК" : "Connect this PC";
    public string JoinPairingTitle => IsRussian ? "Подключение по одноразовому приглашению" : "Connect with a one-time invitation";
    public string JoinPairingHint => IsRussian
        ? "Отсканируйте QR с экрана или вставьте полный sdapp://device-enroll адрес, затем введите код с Admin-устройства."
        : "Scan the QR from the screen or paste the complete sdapp://device-enroll address, then enter the code shown on the Admin device.";
    public string InvitationPayloadLabel => IsRussian ? "Одноразовое приглашение" : "One-time invitation";
    public string ScanInvitationLabel => IsRussian ? "Сканировать QR с экрана" : "Scan QR from screen";
    public string JoinVerificationCodeLabel => IsRussian ? "Шестизначный код" : "6-digit verification code";
    public string JoinVaultPasswordLabel => IsRussian ? "Пароль локального vault (только offline)" : "Local vault password (offline only)";
    public string PairingTitle => IsRussian ? "Одноразовое подключение" : "One-time device pairing";
    public string PairingRoleLabel => IsRussian ? "Роль устройства" : "Device role";
    public string CreatePairingQrLabel => IsRussian ? "Создать одноразовый QR" : "Create one-time QR";
    public string ShowVerificationCodeLabel => IsRussian ? "Показать код подтверждения" : "Show verification code";
    public string PairingVerificationLabel => IsRussian ? "Код подтверждения" : "Verification code";
    public IReadOnlyList<string> PairingRoleOptions => IsRussian
        ? ["Trusted online", "Trusted offline", "Temporary (24 часа)", "Admin"]
        : ["Trusted online", "Trusted offline", "Temporary (24 hours)", "Admin"];
    public bool IsPairingAccountSelectionVisible => SelectedPairingRoleIndex != 3 && PairingQrImage is null;
    public bool IsPairingQrVisible => PairingQrImage is not null;
    public string PairingRoleHint => SelectedPairingRoleIndex switch
    {
        1 => IsRussian ? "Выбранные maFile передаются только внутри зашифрованного grant. Remote wipe не может отозвать secret, скопированный вне SDA++." : "Selected maFiles are transferred only inside the encrypted grant. Remote wipe cannot revoke a secret copied outside SDA++.",
        2 => IsRussian ? "Доступ автоматически истечёт через 24 часа." : "Access expires automatically after 24 hours.",
        3 => IsRussian ? "Admin получает управление устройствами, но Steam-аккаунты и secrets не передаются." : "Admin receives device management access, but no Steam accounts or secrets are transferred.",
        _ => IsRussian ? "Выберите аккаунты, доступные этому устройству." : "Choose accounts available to this device."
    };
    public IReadOnlyList<string> LanguageOptions { get; } = ["English", "Русский"];
    public IReadOnlyList<string> ThemeOptions { get; } = ["Graphite glass", "Obsidian", "Slate"];
    public IReadOnlyList<string> CloudProviderOptions { get; } =
        ["WebDAV", "S3-compatible (Backblaze B2 / Cloudflare R2)", "Dropbox", "OneDrive Personal", "Google Drive"];
    public bool IsWebDavProvider => SelectedCloudProviderIndex == (int)CloudProviderKind.WebDav;
    public bool IsS3Provider => SelectedCloudProviderIndex == (int)CloudProviderKind.S3Compatible;
    public bool IsDropboxProvider => SelectedCloudProviderIndex == (int)CloudProviderKind.Dropbox;
    public bool IsOneDriveProvider => SelectedCloudProviderIndex == (int)CloudProviderKind.OneDrivePersonal;
    public bool IsGoogleDriveProvider => SelectedCloudProviderIndex == (int)CloudProviderKind.GoogleDrive;
    public bool IsOAuthCloudProvider => IsOneDriveProvider || IsGoogleDriveProvider;
    public string ConnectCloudAccountLabel => IsRussian ? "Подключить аккаунт" : "Connect account";
    public string PushCloudLabel => IsRussian ? "Отправить" : "Push";
    public IReadOnlyList<string> QrCaptureModeOptions =>
        [Text.QrFullDesktop, Text.QrMonitorUnderCursor, Text.QrAreaAroundCursor];
    public bool IsCursorAreaMode => SelectedQrCaptureModeIndex == (int)QrCaptureMode.AreaAroundCursor;

    [ObservableProperty]
    private AccountItemViewModel? _selectedAccount;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private bool _isSettingsVisible;

    [ObservableProperty]
    private bool _isCloudVisible;

    [ObservableProperty]
    private bool _isQrVisible;

    [ObservableProperty]
    private bool _isQrBusy;

    [ObservableProperty]
    private string _qrStatus = string.Empty;

    [ObservableProperty]
    private bool _isConfirmationsVisible;

    [ObservableProperty]
    private bool _isMonitoringVisible;

    [ObservableProperty]
    private bool _isMonitoringBusy;

    [ObservableProperty]
    private string _monitoringStatus = string.Empty;

    [ObservableProperty]
    private bool _isCredentialsVisible;

    [ObservableProperty]
    private bool _isEncryptionVisible;

    [ObservableProperty]
    private bool _isDevicesVisible;

    [ObservableProperty]
    private bool _isRelayCheckBusy;

    [ObservableProperty]
    private string _deviceRole = string.Empty;

    [ObservableProperty]
    private string _deviceGrantedAccounts = "0";

    [ObservableProperty]
    private string _deviceExpires = string.Empty;

    [ObservableProperty]
    private string _deviceRelayEndpoint = string.Empty;

    [ObservableProperty]
    private string _deviceRegistryStatus = string.Empty;

    [ObservableProperty]
    private string _relayHealthStatus = string.Empty;

    [ObservableProperty]
    private bool _isDeviceListBusy;

    [ObservableProperty]
    private string _deviceListStatus = string.Empty;

    [ObservableProperty]
    private bool _isAuditBusy;

    [ObservableProperty]
    private string _auditStatus = string.Empty;

    [ObservableProperty]
    private RegistryDeviceItemViewModel? _selectedRegistryDevice;

    [ObservableProperty]
    private bool _isAdminPinConfigured;

    [ObservableProperty]
    private string _adminPin = string.Empty;

    [ObservableProperty]
    private string _adminPinConfirmation = string.Empty;

    [ObservableProperty]
    private string _adminPinStatus = string.Empty;

    [ObservableProperty]
    private bool _isDeviceActionConfirmationVisible;

    [ObservableProperty]
    private bool _isDeviceActionBusy;

    [ObservableProperty]
    private string _pendingDeviceAction = string.Empty;

    [ObservableProperty]
    private string _deviceActionPrompt = string.Empty;

    [ObservableProperty]
    private string _deviceActionStatus = string.Empty;

    [ObservableProperty]
    private bool _isPairingVisible;

    [ObservableProperty]
    private bool _isPairingBusy;

    [ObservableProperty]
    private int _selectedPairingRoleIndex;

    [ObservableProperty]
    private string _pairingAdminPin = string.Empty;

    [ObservableProperty]
    private string _pairingStatus = string.Empty;

    [ObservableProperty]
    private IImage? _pairingQrImage;

    [ObservableProperty]
    private string _pairingVerificationCode = string.Empty;

    [ObservableProperty]
    private bool _isPairingVerificationVisible;

    [ObservableProperty]
    private string _pairingExpiry = string.Empty;

    [ObservableProperty]
    private bool _isJoinPairingVisible;

    [ObservableProperty]
    private bool _isJoinPairingBusy;

    [ObservableProperty]
    private string _joinPairingPayload = string.Empty;

    [ObservableProperty]
    private string _joinVerificationCode = string.Empty;

    [ObservableProperty]
    private string _joinVaultPassword = string.Empty;

    [ObservableProperty]
    private string _joinPairingStatus = string.Empty;

    [ObservableProperty]
    private bool _isVaultProtectionEncrypted;

    [ObservableProperty]
    private bool _isVaultProtectionBusy;

    [ObservableProperty]
    private bool _isProtectionConfirmationVisible;

    [ObservableProperty]
    private string _vaultProtectionStatus = string.Empty;

    [ObservableProperty]
    private string _vaultProtectionCurrentPassword = string.Empty;

    [ObservableProperty]
    private string _vaultProtectionNewPassword = string.Empty;

    [ObservableProperty]
    private string _vaultProtectionConfirmPassword = string.Empty;

    [ObservableProperty]
    private string _vaultProtectionPrompt = string.Empty;

    private bool _pendingProtectionEnabled;

    [ObservableProperty]
    private string _credentialsStatus = string.Empty;

    [ObservableProperty]
    private string _credentialEditorUsername = string.Empty;

    [ObservableProperty]
    private string _credentialEditorPassword = string.Empty;

    [ObservableProperty]
    private bool _isCredentialEditorVisible;

    [ObservableProperty]
    private bool _isCredentialVaultReady;

    [ObservableProperty]
    private bool _isCredentialVaultMissing;

    public bool IsCredentialEmptyStateVisible => IsCredentialVaultReady && CredentialAccounts.Count == 0 && !IsCredentialEditorVisible;

    [ObservableProperty]
    private bool _isPlainCredentialImportWarningVisible;

    [ObservableProperty]
    private bool _isCredentialVaultDeleteConfirmationVisible;

    private string _plainCredentialImportSourcePath = string.Empty;

    [ObservableProperty]
    private bool _isCredentialSetupVisible;

    [ObservableProperty]
    private bool _canContinueCredentialSetup;

    [ObservableProperty]
    private bool _isCredentialKeyReentryVisible;

    [ObservableProperty]
    private bool _isCredentialFinalWarningVisible;

    [ObservableProperty]
    private string _generatedCredentialKey = string.Empty;

    [ObservableProperty]
    private string _credentialKeyConfirmation = string.Empty;

    [ObservableProperty]
    private string _credentialSetupStatus = string.Empty;

    [ObservableProperty]
    private bool _isCredentialImportVisible;

    [ObservableProperty]
    private string _credentialImportSourcePath = string.Empty;

    [ObservableProperty]
    private string _credentialImportKey = string.Empty;

    [ObservableProperty]
    private string _credentialImportStatus = string.Empty;

    [ObservableProperty]
    private bool _storeLoginCredential;

    [ObservableProperty]
    private bool _hasStoredLoginCredential;

    [ObservableProperty]
    private bool _isAutoLoginAllPanelVisible;

    [ObservableProperty]
    private bool _isAutoLoginAllBusy;

    [ObservableProperty]
    private string _autoLoginAllVaultPassword = string.Empty;

    [ObservableProperty]
    private string _autoLoginAllStatus = string.Empty;

    [ObservableProperty]
    private bool _canRunAutoLoginAll;

    [ObservableProperty]
    private bool _autoLoginAllNeedsVaultPassword;

    [ObservableProperty]
    private bool _isConfirmationBusy;

    [ObservableProperty]
    private string _confirmationsStatus = string.Empty;

    [ObservableProperty]
    private bool _isConfirmationActionPending;

    [ObservableProperty]
    private string _confirmationActionPrompt = string.Empty;

    private ConfirmationItemViewModel? _pendingConfirmation;
    private ConfirmationDecision _pendingConfirmationDecision;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private int _selectedLanguageIndex;

    [ObservableProperty]
    private int _selectedThemeIndex;

    [ObservableProperty]
    private bool _accountSearchEnabled = true;

    [ObservableProperty]
    private bool _favoritesFirst = true;

    [ObservableProperty]
    private bool _periodicConfirmationChecking;

    [ObservableProperty]
    private int _confirmationCheckIntervalSeconds = 30;

    [ObservableProperty]
    private bool _checkAllAccountsForConfirmations;

    [ObservableProperty]
    private bool _autoConfirmMarketTransactions;

    [ObservableProperty]
    private bool _autoConfirmGiftTrades;

    [ObservableProperty]
    private string _confirmationPollingStatus = string.Empty;

    [ObservableProperty]
    private int _selectedQrCaptureModeIndex;

    [ObservableProperty]
    private int _qrCursorAreaSize = 700;

    [ObservableProperty]
    private bool _globalHotkeysEnabled = true;

    [ObservableProperty]
    private bool _qrHotkeysEnabled = true;

    [ObservableProperty]
    private string _qrToggleHotkey = "Ctrl+Shift+Q";

    [ObservableProperty]
    private string _qrScanHotkey = "Ctrl+Shift+S";

    [ObservableProperty]
    private string _previousAccountHotkey = "Ctrl+Shift+Left";

    [ObservableProperty]
    private string _nextAccountHotkey = "Ctrl+Shift+Right";

    [ObservableProperty]
    private string _autoLoginAllHotkey = "Ctrl+Shift+L";

    [ObservableProperty]
    private string _confirmationsHotkey = "Ctrl+Shift+C";

    [ObservableProperty]
    private string _hotkeyStatus = string.Empty;

    [ObservableProperty]
    private string _settingsNotice = string.Empty;

    [ObservableProperty]
    private string _webDavEndpoint = string.Empty;

    [ObservableProperty]
    private string _webDavUsername = string.Empty;

    [ObservableProperty]
    private string _webDavPassword = string.Empty;

    [ObservableProperty]
    private string _webDavRemotePath = "SDAppVault";

    [ObservableProperty]
    private int _selectedCloudProviderIndex;

    [ObservableProperty]
    private string _s3Endpoint = string.Empty;

    [ObservableProperty]
    private string _s3Bucket = string.Empty;

    [ObservableProperty]
    private string _s3AccessKey = string.Empty;

    [ObservableProperty]
    private string _s3SecretKey = string.Empty;

    [ObservableProperty]
    private string _s3RemotePath = "SDAppVault";

    [ObservableProperty]
    private string _dropboxAccessToken = string.Empty;

    [ObservableProperty]
    private string _dropboxRemotePath = "SDAppVault";

    [ObservableProperty]
    private string _oneDriveClientId = string.Empty;

    [ObservableProperty]
    private string _oneDriveRemotePath = "SDAppVault";

    [ObservableProperty]
    private string _googleDriveClientId = string.Empty;

    [ObservableProperty]
    private string _googleDriveRemotePath = "SDAppVault";

    [ObservableProperty]
    private string _cloudStatus = string.Empty;

    [ObservableProperty]
    private string _cloudPreviewSummary = string.Empty;

    [ObservableProperty]
    private bool _isCloudBusy;

    [ObservableProperty]
    private bool _isImportConfirmationVisible;

    [ObservableProperty]
    private string _importConfirmationText = string.Empty;

    [ObservableProperty]
    private string _vaultPassword = string.Empty;

    [ObservableProperty]
    private bool _isVaultLocked;

    [ObservableProperty]
    private bool _isVaultError;

    [ObservableProperty]
    private string _vaultStateTitle = string.Empty;

    [ObservableProperty]
    private string _vaultStateDetail = string.Empty;

    [ObservableProperty]
    private bool _isLoginPanelVisible;

    [ObservableProperty]
    private bool _isLoginBusy;

    [ObservableProperty]
    private string _loginUsername = string.Empty;

    [ObservableProperty]
    private string _loginPassword = string.Empty;

    [ObservableProperty]
    private string _loginVaultPassword = string.Empty;

    [ObservableProperty]
    private string _loginStatus = string.Empty;

    [ObservableProperty]
    private bool _isLoginEmailCodeRequested;

    [ObservableProperty]
    private string _loginEmailCode = string.Empty;

    [ObservableProperty]
    private string _loginEmailPrompt = string.Empty;

    [ObservableProperty]
    private bool _isEndSessionsPanelVisible;

    [ObservableProperty]
    private bool _isEndSessionsBusy;

    [ObservableProperty]
    private string _endSessionsVaultPassword = string.Empty;

    [ObservableProperty]
    private string _endSessionsPrompt = string.Empty;

    [ObservableProperty]
    private string _endSessionsStatus = string.Empty;

    [ObservableProperty]
    private bool _isSteamSessionsPanelVisible;

    [ObservableProperty]
    private bool _isSteamSessionsBusy;

    [ObservableProperty]
    private string _steamSessionsStatus = string.Empty;

    [ObservableProperty]
    private bool _isAccountImportVisible;

    [ObservableProperty]
    private bool _isAccountImportBusy;

    [ObservableProperty]
    private string _accountImportSourcePath = string.Empty;

    [ObservableProperty]
    private string _accountImportSourcePassword = string.Empty;

    [ObservableProperty]
    private string _accountImportDestinationPassword = string.Empty;

    [ObservableProperty]
    private string _accountImportStatus = string.Empty;

    [RelayCommand]
    private void ShowDashboard() => SetPage(settings: false, cloud: false, qr: false, confirmations: false);

    [RelayCommand]
    private async Task PickAccountFileAsync()
    {
        string? path = await _accountFilePicker.PickMaFileAsync();
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        AccountImportSourcePath = path;
        AccountImportSourcePassword = string.Empty;
        AccountImportDestinationPassword = string.Empty;
        AccountImportStatus = string.Empty;
        IsAccountImportVisible = true;
    }

    [RelayCommand]
    private void CancelAccountImport()
    {
        if (IsAccountImportBusy)
        {
            return;
        }

        ClearAccountImportSecrets();
        AccountImportStatus = string.Empty;
        IsAccountImportVisible = false;
    }

    [RelayCommand]
    private void ConfirmAccountImport()
    {
        if (IsAccountImportBusy || string.IsNullOrWhiteSpace(AccountImportSourcePath))
        {
            return;
        }

        string sourcePassword = AccountImportSourcePassword;
        string destinationPassword = AccountImportDestinationPassword;
        IsAccountImportBusy = true;
        try
        {
            VaultAccountImportResult result = _accountImportService.Import(
                AccountImportSourcePath,
                _maFilesDirectory,
                sourcePassword,
                destinationPassword);
            if (!result.IsSuccess)
            {
                AccountImportStatus = result.State switch
                {
                    VaultAccountImportState.SourceLocked => Text.ImportSourceLocked,
                    VaultAccountImportState.InvalidSourcePassword => Text.ImportWrongSourcePassword,
                    VaultAccountImportState.DestinationLocked => Text.VaultLockedDetail,
                    VaultAccountImportState.InvalidDestinationPassword => Text.WrongPasswordDetail,
                    VaultAccountImportState.Duplicate => Text.ImportDuplicate,
                    VaultAccountImportState.InvalidAccount => result.Detail ?? Text.ImportInvalidAccount,
                    _ => result.Detail ?? Text.ImportFailed
                };
                return;
            }

            VaultLoadResult refreshed = _vaultReader.Load(_maFilesDirectory, destinationPassword);
            ApplyVaultResult(refreshed);
            StatusMessage = string.Format(Text.AccountImportedFormat, result.AccountName);
            IsAccountImportVisible = false;
        }
        finally
        {
            ClearAccountImportSecrets();
            IsAccountImportBusy = false;
        }
    }

    [RelayCommand]
    private void ShowSettings() => SetPage(settings: true, cloud: false, qr: false, confirmations: false);

    [RelayCommand]
    private void ShowCloud() => SetPage(settings: false, cloud: true, qr: false, confirmations: false);

    [RelayCommand]
    private void ShowQr() => SetPage(settings: false, cloud: false, qr: true, confirmations: false);

    [RelayCommand]
    private async Task ShowMonitoringAsync()
    {
        SetPage(settings: false, cloud: false, qr: false, confirmations: false, monitoring: true);
        if (MonitoringAccounts.Count == 0)
        {
            await RefreshMonitoringAsync();
        }
    }

    [RelayCommand]
    private void ShowCredentials()
    {
        SetPage(settings: false, cloud: false, qr: false, confirmations: false, credentials: true);
        RefreshStoredCredentials();
        PrepareCredentialEditor(SelectedAccount);
        IsCredentialEditorVisible = false;
    }

    [RelayCommand]
    private void ShowEncryption()
    {
        SetPage(settings: false, cloud: false, qr: false, confirmations: false, encryption: true);
        RefreshVaultProtectionStatus();
    }

    [RelayCommand]
    private async Task ShowDevicesAsync()
    {
        SetPage(settings: false, cloud: false, qr: false, confirmations: false, devices: true);
        RefreshDeviceRegistry();
        if (_deviceRegistry.State == DeviceRegistryLoadState.Ready && _deviceRegistry.Settings.IsConfigured)
        {
            await RefreshDeviceListAsync();
            await RefreshAuditAsync();
        }
    }

    [RelayCommand]
    private async Task RefreshDeviceListAsync()
    {
        if (IsDeviceListBusy) return;
        RegistryDevices.Clear();
        if (_deviceRegistryClient is null)
        {
            DeviceListStatus = IsRussian
                ? "Существующий ключ подписи устройства не найден для этого пользователя Windows."
                : "The existing device signing key was not found for this Windows user.";
            return;
        }

        IsDeviceListBusy = true;
        DeviceListStatus = IsRussian ? "Получение подписанного списка..." : "Loading the signed device list...";
        try
        {
            DeviceListResult result = await _deviceRegistryClient.ListDevicesAsync(_deviceRegistry.Settings);
            if (!result.Success)
            {
                DeviceListStatus = result.Detail ?? (IsRussian ? "Не удалось получить устройства." : "Could not load devices.");
                return;
            }

            foreach (RegistryDevice device in result.Devices.OrderByDescending(device => device.Role == "Admin").ThenBy(device => device.DisplayName, StringComparer.OrdinalIgnoreCase))
                RegistryDevices.Add(new RegistryDeviceItemViewModel(device, IsRussian));
            DeviceListStatus = IsRussian
                ? $"Устройств: {RegistryDevices.Count}."
                : $"Devices: {RegistryDevices.Count}.";
        }
        finally
        {
            IsDeviceListBusy = false;
        }
    }

    [RelayCommand]
    private void ConfigureAdminPin()
    {
        if (_deviceRegistry.Settings.Role != "Admin")
        {
            AdminPinStatus = IsRussian ? "Admin PIN можно настроить только на Admin-устройстве." : "Admin PIN can only be configured on the Admin device.";
            return;
        }

        AdminPinResult result = _adminPinService.Configure(_deviceRegistry.Settings, AdminPin, AdminPinConfirmation);
        ClearAdminPinFields();
        if (result.State == AdminPinState.Success)
        {
            IsAdminPinConfigured = true;
            OnPropertyChanged(nameof(IsAdminPinSetupVisible));
            AdminPinStatus = IsRussian ? "Admin PIN настроен." : "Admin PIN configured.";
        }
        else
        {
            AdminPinStatus = IsRussian ? "Используйте одинаковые PIN из 6–12 цифр." : "Use matching PINs containing 6–12 digits.";
        }
    }

    [RelayCommand]
    private void PrepareDeviceAction(string action)
    {
        if (IsDeviceActionBusy) return;
        _deviceActionApproval = null;
        DeviceActionStatus = string.Empty;
        if (_deviceRegistry.Settings.Role != "Admin")
        {
            DeviceActionStatus = IsRussian ? "Управление доступно только Admin-устройству." : "Device management is available only to the Admin device.";
            return;
        }
        if (!IsAdminPinConfigured)
        {
            DeviceActionStatus = IsRussian ? "Сначала создайте Admin PIN." : "Create an Admin PIN first.";
            return;
        }
        if (SelectedRegistryDevice is null)
        {
            DeviceActionStatus = IsRussian ? "Выберите устройство." : "Select a device.";
            return;
        }
        if (SelectedRegistryDevice.Role == "Admin")
        {
            DeviceActionStatus = IsRussian ? "Удалённые команды нельзя применять к Admin-устройству." : "Remote commands cannot target the Admin device.";
            return;
        }
        if (SelectedRegistryDevice.Device.RevokedAt is not null)
        {
            DeviceActionStatus = IsRussian ? "Это устройство уже отозвано." : "This device is already revoked.";
            return;
        }
        if (action is not ("Lock" or "Revoke" or "WipeLocalData")) return;

        PendingDeviceAction = action;
        _deviceActionApproval = new DeviceActionApproval(_deviceRegistry.Settings.VaultId,
            _deviceRegistry.Settings.RelayUrl, SelectedRegistryDevice.DeviceId, action);
        DeviceActionPrompt = action switch
        {
            "Lock" => IsRussian ? $"Поставить блокировку для «{SelectedRegistryDevice.Name}»?" : $"Queue a lock for “{SelectedRegistryDevice.Name}”?",
            "Revoke" => IsRussian ? $"Безвозвратно отозвать доступ «{SelectedRegistryDevice.Name}»?" : $"Permanently revoke access for “{SelectedRegistryDevice.Name}”?",
            _ => IsRussian ? $"Удалить локальные данные SDA++ на «{SelectedRegistryDevice.Name}» при следующей связи?" : $"Wipe local SDA++ data on “{SelectedRegistryDevice.Name}” at its next contact?"
        };
        AdminPin = string.Empty;
        IsDeviceActionConfirmationVisible = true;
    }

    [RelayCommand]
    private void CancelDeviceAction()
    {
        if (IsDeviceActionBusy) return;
        AdminPin = string.Empty;
        PendingDeviceAction = string.Empty;
        _deviceActionApproval = null;
        DeviceActionPrompt = string.Empty;
        IsDeviceActionConfirmationVisible = false;
    }

    [RelayCommand]
    private async Task ConfirmDeviceActionAsync()
    {
        DeviceActionApproval? approval = _deviceActionApproval;
        if (IsDeviceActionBusy || !IsDeviceActionConfirmationVisible || approval is null || _deviceRegistryClient is null) return;
        RegistryDeviceItemViewModel? target = RegistryDevices.FirstOrDefault(device => device.DeviceId == approval.DeviceId);
        if (!approval.Allows(_deviceRegistry.Settings, target?.Device))
        {
            AdminPin = string.Empty;
            DeviceActionStatus = IsRussian ? "Устройство или права изменились. Откройте подтверждение заново." : "Device or permissions changed. Open a new confirmation.";
            return;
        }
        IsDeviceActionBusy = true;
        try
        {
            AdminPinResult pinResult = _adminPinService.Verify(_deviceRegistry.Settings, AdminPin);
            AdminPin = string.Empty;
            if (pinResult.State != AdminPinState.Success)
            {
                DeviceActionStatus = pinResult.State switch
                {
                    AdminPinState.Locked => IsRussian ? $"Слишком много попыток. Повторите через {pinResult.RetryAfterSeconds} сек." : $"Too many attempts. Try again in {pinResult.RetryAfterSeconds} seconds.",
                    AdminPinState.Damaged => IsRussian ? "Данные Admin PIN повреждены." : "Admin PIN data is damaged.",
                    _ => IsRussian ? "Неверный Admin PIN." : "Wrong Admin PIN."
                };
                return;
            }

            DeviceCommandResult result = approval.Action == "Revoke"
                ? await _deviceRegistryClient.RevokeAsync(_deviceRegistry.Settings, approval.DeviceId)
                : await _deviceRegistryClient.IssueCommandAsync(_deviceRegistry.Settings, approval.DeviceId, approval.Action);
            DeviceActionStatus = result.Success
                ? (approval.Action == "Revoke"
                    ? (IsRussian ? "Сервер принял отзыв доступа SDA++. Это не завершает Steam-сессии и не удаляет ранее скопированные данные." : "The server accepted SDA++ access revocation. This does not terminate Steam sessions or erase previously copied data.")
                    : (IsRussian ? "Сервер принял команду. Выполнение на устройстве пока не подтверждено." : "The server accepted the command. Execution on the device is not confirmed yet."))
                : result.Detail ?? (IsRussian ? "Команда не отправлена." : "The command was not sent.");
            if (!result.Success) return;

            IsDeviceActionConfirmationVisible = false;
            PendingDeviceAction = string.Empty;
            _deviceActionApproval = null;
            await RefreshDeviceListAsync();
            await RefreshAuditAsync();
        }
        finally
        {
            IsDeviceActionBusy = false;
        }
    }

    [RelayCommand]
    private void OpenPairing()
    {
        PairingStatus = string.Empty;
        if (_deviceRegistry.Settings.Role != "Admin")
        {
            DeviceActionStatus = IsRussian ? "Новые устройства может добавлять только Admin." : "Only the Admin device can add new devices.";
            return;
        }
        if (!IsAdminPinConfigured)
        {
            DeviceActionStatus = IsRussian ? "Сначала создайте Admin PIN." : "Create an Admin PIN first.";
            return;
        }

        PairingAccounts.Clear();
        foreach (AccountItemViewModel account in _allAccounts.OrderBy(account => account.Name, StringComparer.OrdinalIgnoreCase))
            PairingAccounts.Add(new PairingAccountItemViewModel(account.Account));
        SelectedPairingRoleIndex = 0;
        PairingAdminPin = string.Empty;
        PairingVerificationCode = string.Empty;
        PairingExpiry = string.Empty;
        IsPairingVerificationVisible = false;
        DisposePairingQr();
        IsPairingVisible = true;
    }

    [RelayCommand]
    private void OpenJoinPairing()
    {
        RecipientIdentityCode = EnsureEnrollmentClient()?.RecipientCode ?? string.Empty;
        JoinPairingPayload = string.Empty;
        JoinVerificationCode = string.Empty;
        JoinVaultPassword = string.Empty;
        JoinPairingStatus = string.Empty;
        IsJoinPairingVisible = true;
    }

    [RelayCommand]
    private void CloseJoinPairing()
    {
        if (IsJoinPairingBusy) return;
        ClearJoinPairingSecrets();
        IsJoinPairingVisible = false;
    }

    [RelayCommand]
    private async Task ScanJoinPairingQrAsync()
    {
        if (IsJoinPairingBusy) return;
        IsJoinPairingBusy = true;
        JoinPairingStatus = IsRussian ? "Поиск QR на экране..." : "Looking for a QR code on screen...";
        try
        {
            string? payload = await _screenQrScanner.ScanAsync(
                (QrCaptureMode)SelectedQrCaptureModeIndex,
                QrCursorAreaSize);
            if (string.IsNullOrWhiteSpace(payload))
            {
                JoinPairingStatus = IsRussian ? "QR-код не найден." : "No QR code was found.";
                return;
            }
            if (!payload.StartsWith("sdapp://device-enroll?", StringComparison.OrdinalIgnoreCase))
            {
                JoinPairingStatus = IsRussian ? "Найденный QR не является приглашением SDA++." : "The detected QR is not an SDA++ device invitation.";
                return;
            }
            JoinPairingPayload = payload;
            JoinPairingStatus = IsRussian ? "Приглашение считано. Введите код с Admin-устройства." : "Invitation scanned. Enter the code shown on the Admin device.";
        }
        catch (Exception ex) when (ex is InvalidOperationException or PlatformNotSupportedException or System.ComponentModel.Win32Exception)
        {
            JoinPairingStatus = ex.Message;
        }
        finally
        {
            IsJoinPairingBusy = false;
        }
    }

    [RelayCommand]
    private async Task ClaimPairingInvitationAsync()
    {
        if (IsJoinPairingBusy || string.IsNullOrWhiteSpace(JoinPairingPayload)) return;
        IDeviceRegistryClient? client = EnsureEnrollmentClient();
        if (client is null)
        {
            JoinPairingStatus = IsRussian ? "Не удалось создать защищённый ключ этого устройства." : "Could not create this device's protected identity.";
            return;
        }

        IsJoinPairingBusy = true;
        JoinPairingStatus = IsRussian ? "Проверка подписи Admin и получение разрешений..." : "Verifying the Admin signature and receiving permissions...";
        DeviceClaimResult? claim = null;
        try
        {
            claim = await client.ClaimInvitationAsync(
                JoinPairingPayload.Trim(),
                JoinVerificationCode,
                Environment.MachineName);
            if (!claim.Success || claim.Settings is null)
            {
                JoinPairingStatus = claim.Detail ?? (IsRussian ? "Подключение не выполнено." : "The device could not be connected.");
                return;
            }

            var currentTrust = _deviceRegistryStore.Load().Settings;
            if (!string.IsNullOrEmpty(currentTrust.E2EGrant))
            {
                var oldGrant = EndToEndTrust.Verify<DeviceGrantV2>(currentTrust.E2EGrant, currentTrust.AdminPublicKey);
                var newGrant = EndToEndTrust.Verify<DeviceGrantV2>(claim.Settings.E2EGrant, claim.Settings.AdminPublicKey);
                if (currentTrust.AdminPublicKey != claim.Settings.AdminPublicKey || currentTrust.VaultId != claim.Settings.VaultId ||
                    newGrant.Revision < oldGrant.Revision || (newGrant.Revision == oldGrant.Revision && newGrant.GrantId != oldGrant.GrantId))
                {
                    JoinPairingStatus = "Trust downgrade or Admin identity replacement rejected.";
                    return;
                }
            }
            if (!ImportGrantedAccounts(claim.Accounts ?? [], JoinVaultPassword, out string? importError))
            {
                JoinPairingStatus = importError ?? (IsRussian ? "Не удалось импортировать offline-аккаунты." : "The offline accounts could not be imported.");
                return;
            }

            _deviceRegistryStore.Save(claim.Settings);
            _deviceRegistry = _deviceRegistryStore.Load();
            ApplyVaultResult(_vaultReader.Load(_maFilesDirectory, JoinVaultPassword));
            RefreshDeviceRegistry();
            JoinPairingStatus = IsRussian ? "Устройство подключено, разрешения проверены." : "Device connected and permissions verified.";
            ClearJoinPairingSecrets();
            IsJoinPairingVisible = false;
            await RefreshDeviceListAsync();
        }
        catch (Exception ex) when (ex is CryptographicException or IOException or UnauthorizedAccessException)
        {
            JoinPairingStatus = ex.Message;
        }
        finally
        {
            if (claim?.Accounts is not null)
                foreach (GrantedAccountPayload account in claim.Accounts)
                    CryptographicOperations.ZeroMemory(account.MaFilePayload);
            IsJoinPairingBusy = false;
        }
    }

    [RelayCommand]
    private void ClosePairing()
    {
        if (IsPairingBusy) return;
        PairingAdminPin = string.Empty;
        PairingVerificationCode = string.Empty;
        PairingAccounts.Clear();
        DisposePairingQr();
        IsPairingVisible = false;
    }

    [RelayCommand]
    private async Task CreatePairingInvitationAsync()
    {
        if (IsPairingBusy || _deviceRegistryClient is null) return;
        AdminPinResult pinResult = _adminPinService.Verify(_deviceRegistry.Settings, PairingAdminPin);
        PairingAdminPin = string.Empty;
        if (pinResult.State != AdminPinState.Success)
        {
            PairingStatus = pinResult.State == AdminPinState.Locked
                ? (IsRussian ? $"Слишком много попыток. Повторите через {pinResult.RetryAfterSeconds} сек." : $"Too many attempts. Try again in {pinResult.RetryAfterSeconds} seconds.")
                : (IsRussian ? "Неверный Admin PIN." : "Wrong Admin PIN.");
            return;
        }

        string role = SelectedPairingRoleIndex switch
        {
            1 => "TrustedOffline",
            2 => "Temporary",
            3 => "Admin",
            _ => "TrustedOnline"
        };
        PairingAccountItemViewModel[] selected = role == "Admin"
            ? []
            : PairingAccounts.Where(account => account.IsSelected).ToArray();
        if (role != "Admin" && selected.Length == 0)
        {
            PairingStatus = IsRussian ? "Выберите хотя бы один аккаунт." : "Select at least one account.";
            return;
        }

        List<byte[]> sensitivePayloads = [];
        List<DeviceInvitationAccount> invitationAccounts = [];
        try
        {
            foreach (PairingAccountItemViewModel account in selected)
            {
                byte[]? payload = null;
                if (role == "TrustedOffline")
                {
                    payload = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(account.Account.GuardAccount));
                    sensitivePayloads.Add(payload);
                }
                invitationAccounts.Add(new DeviceInvitationAccount(account.SteamId, payload));
            }

            IsPairingBusy = true;
            PairingStatus = IsRussian ? "Создание зашифрованного grant..." : "Creating the encrypted grant...";
            DeviceInvitationResult result = await _deviceRegistryClient.CreateBoundInvitationAsync(
                _deviceRegistry.Settings,
                PairingRecipientCode.Trim(),
                role,
                invitationAccounts,
                role == "Admin" ? [] : ["ViewGuardCode", "ApproveSteamQr", "ViewConfirmations", "ApproveConfirmations"]);
            if (!result.Success || string.IsNullOrWhiteSpace(result.QrPayload))
            {
                PairingStatus = result.Detail ?? (IsRussian ? "Не удалось создать приглашение." : "Could not create the invitation.");
                return;
            }

            DisposePairingQr();
            PairingQrImage = _qrCodeRenderer.Render(result.QrPayload, 320);
            PairingVerificationCode = result.VerificationCode ?? string.Empty;
            PairingExpiry = DateTimeOffset.FromUnixTimeMilliseconds(result.ExpiresAt).ToLocalTime().ToString("T");
            IsPairingVerificationVisible = false;
            PairingStatus = IsRussian
                ? $"E2E v2 QR до {PairingExpiry}. Передайте напрямую указанному получателю. Требуется клиент с поддержкой device enrollment v2."
                : $"E2E v2 QR expires at {PairingExpiry}. Transfer directly to the specified recipient running a v2 enrollment client.";
        }
        finally
        {
            foreach (byte[] payload in sensitivePayloads)
                CryptographicOperations.ZeroMemory(payload);
            IsPairingBusy = false;
        }
    }

    [RelayCommand]
    private void ShowPairingVerificationCode() => IsPairingVerificationVisible = true;

    [RelayCommand]
    private async Task RefreshAuditAsync()
    {
        if (IsAuditBusy) return;
        RegistryAuditEvents.Clear();
        if (_deviceRegistryClient is null)
        {
            AuditStatus = IsRussian ? "Ключ подписи устройства недоступен." : "The device signing key is unavailable.";
            return;
        }

        IsAuditBusy = true;
        AuditStatus = IsRussian ? "Загрузка журнала..." : "Loading security activity...";
        try
        {
            DeviceAuditResult result = await _deviceRegistryClient.ListAuditAsync(_deviceRegistry.Settings);
            if (!result.Success)
            {
                AuditStatus = result.Detail ?? (IsRussian ? "Не удалось получить журнал." : "Could not load security activity.");
                return;
            }

            foreach (RegistryAuditEvent audit in result.Events.OrderByDescending(item => item.OccurredAt).Take(200))
                RegistryAuditEvents.Add(new RegistryAuditItemViewModel(audit));
            AuditStatus = IsRussian
                ? $"Событий: {RegistryAuditEvents.Count}. Это журнал relay, не подписанное подтверждение исполнения устройства."
                : $"Events: {RegistryAuditEvents.Count}. Relay activity is not a device-signed execution result.";
        }
        finally
        {
            IsAuditBusy = false;
        }
    }

    [RelayCommand]
    private void RefreshDeviceRegistry()
    {
        _deviceRegistry = _deviceRegistryStore.Load();
        DeviceRegistrySettings settings = _deviceRegistry.Settings;
        DeviceRole = settings.IsConfigured
            ? settings.Role
            : (IsRussian ? "Не настроено" : "Not configured");
        DeviceGrantedAccounts = settings.GrantedAccountSteamIds.Count.ToString(System.Globalization.CultureInfo.CurrentCulture);
        DeviceExpires = settings.ExpiresAt is long expires
            ? DateTimeOffset.FromUnixTimeMilliseconds(expires).ToLocalTime().ToString("g")
            : "—";
        DeviceRelayEndpoint = string.IsNullOrWhiteSpace(settings.RelayUrl)
            ? PairingRelayService.ManagedEndpoint
            : settings.RelayUrl;
        DeviceRegistryStatus = _deviceRegistry.State switch
        {
            DeviceRegistryLoadState.Ready when settings.IsExpired => IsRussian ? "Срок доступа этого устройства истёк." : "This device access has expired.",
            DeviceRegistryLoadState.Ready => IsRussian ? "Защищённый registry загружен." : "Secure registry loaded.",
            DeviceRegistryLoadState.Missing => IsRussian ? "Устройство ещё не зарегистрировано." : "This device is not registered yet.",
            DeviceRegistryLoadState.UnsupportedPlatform => IsRussian ? "Registry защищён Windows DPAPI и недоступен на этой системе." : "The registry is protected by Windows DPAPI and is unavailable on this system.",
            _ => IsRussian ? "Не удалось открыть защищённый registry." : "The secure registry could not be opened."
        };
        IsAdminPinConfigured = AdminPinService.IsConfigured(settings);
        OnPropertyChanged(nameof(IsAdminPinSetupVisible));
        RelayHealthStatus = string.Empty;
    }

    [RelayCommand]
    private async Task CheckPairingRelayAsync()
    {
        if (IsRelayCheckBusy) return;
        IsRelayCheckBusy = true;
        try
        {
            PairingRelayHealthResult result = await _pairingRelayService.CheckHealthAsync(DeviceRelayEndpoint);
            RelayHealthStatus = result.IsCompatible
                ? (IsRussian ? $"Worker совместим. Протокол v{result.ProtocolVersion}, хранение: только шифротекст." : $"Worker compatible. Protocol v{result.ProtocolVersion}, ciphertext-only storage.")
                : (result.Detail ?? (IsRussian ? "Worker несовместим." : "Worker is incompatible."));
        }
        catch (ArgumentException ex)
        {
            RelayHealthStatus = ex.Message;
        }
        finally
        {
            IsRelayCheckBusy = false;
        }
    }

    [RelayCommand]
    private void PrepareEnableEncryption() => PrepareProtectionChange(enable: true, requireCurrentPassword: false);

    [RelayCommand]
    private void PrepareChangeEncryption() => PrepareProtectionChange(enable: true, requireCurrentPassword: true);

    [RelayCommand]
    private void PrepareDisableEncryption() => PrepareProtectionChange(enable: false, requireCurrentPassword: true);

    [RelayCommand]
    private void CancelProtectionChange()
    {
        if (IsVaultProtectionBusy) return;
        IsProtectionConfirmationVisible = false;
        VaultProtectionPrompt = string.Empty;
        ClearVaultProtectionSecrets();
    }

    [RelayCommand]
    private async Task ApplyProtectionChangeAsync()
    {
        if (IsVaultProtectionBusy || !IsProtectionConfirmationVisible) return;

        string currentPassword = VaultProtectionCurrentPassword;
        string newPassword = VaultProtectionNewPassword;
        IsVaultProtectionBusy = true;
        VaultProtectionStatus = IsRussian ? "Проверка и подготовка хранилища..." : "Validating and staging the vault...";
        try
        {
            VaultEncryptionResult result = await Task.Run(() => _vaultEncryptionService.ChangeProtection(
                _maFilesDirectory,
                currentPassword,
                _pendingProtectionEnabled ? newPassword : null));
            if (!result.Success)
            {
                VaultProtectionStatus = result.State switch
                {
                    VaultEncryptionState.InvalidPassword => IsRussian ? "Неверный текущий пароль. Файлы не изменены." : "The current password is incorrect. No files were changed.",
                    VaultEncryptionState.Corrupt => result.Detail ?? (IsRussian ? "Хранилище повреждено." : "The vault is corrupt."),
                    _ => result.Detail ?? (IsRussian ? "Не удалось изменить защиту." : "Could not change vault protection.")
                };
                return;
            }

            IsVaultProtectionEncrypted = result.IsEncrypted;
            VaultProtectionStatus = IsRussian
                ? $"Готово. Обработано аккаунтов: {result.AccountCount}. Резервная копия: {result.BackupDirectory}"
                : $"Complete. Accounts processed: {result.AccountCount}. Backup: {result.BackupDirectory}";
            ApplyVaultResult(_vaultReader.Load(_maFilesDirectory, result.IsEncrypted ? newPassword : null));
            IsProtectionConfirmationVisible = false;
        }
        finally
        {
            ClearVaultProtectionSecrets();
            IsVaultProtectionBusy = false;
        }
    }

    [RelayCommand]
    private void RemoveCredential(CredentialItemViewModel? item)
    {
        if (item is null) return;
        CredentialVaultWriteResult result = _credentialVault.Remove(item.CreateIdentity());
        CredentialsStatus = result.IsSuccess
            ? (IsRussian ? "Сохранённые данные удалены." : "Saved credentials removed.")
            : result.Detail ?? (IsRussian ? "Не удалось удалить данные." : "Could not remove credentials.");
        RefreshStoredCredentials(preserveStatus: true);
    }

    [RelayCommand]
    private void SaveCredential()
    {
        if (SelectedAccount is null)
        {
            CredentialsStatus = IsRussian ? "Сначала выберите аккаунт." : "Select an account first.";
            return;
        }
        if (string.IsNullOrWhiteSpace(CredentialEditorUsername) || string.IsNullOrEmpty(CredentialEditorPassword))
        {
            CredentialsStatus = IsRussian ? "Введите логин и пароль Steam." : "Enter the Steam username and password.";
            return;
        }

        CredentialVaultWriteResult result = _credentialVault.Save(
            SelectedAccount.Account,
            CredentialEditorUsername,
            CredentialEditorPassword);
        CredentialEditorPassword = string.Empty;
        CredentialsStatus = result.IsSuccess
            ? (IsRussian ? $"Логин для «{SelectedAccount.Name}» сохранён в зашифрованном файле." : $"Credentials for “{SelectedAccount.Name}” were saved to the encrypted vault.")
            : result.Detail ?? (IsRussian ? "Не удалось сохранить логин." : "Could not save the credentials.");
        if (result.IsSuccess) IsCredentialEditorVisible = false;
        RefreshStoredCredentials(preserveStatus: true);
    }

    [RelayCommand]
    private void SaveAccountCredential(AccountCredentialRowViewModel? row)
    {
        if (row is null) return;
        if (string.IsNullOrEmpty(row.Password))
        {
            CredentialsStatus = IsRussian ? "Введите пароль Steam для выбранного аккаунта." : "Enter the Steam password for this account.";
            return;
        }

        CredentialVaultWriteResult result = _credentialVault.Save(row.Account.Account, row.Username, row.Password);
        row.ClearPassword();
        if (result.IsSuccess)
        {
            row.SetSaved(true);
            CredentialsStatus = IsRussian
                ? $"Пароль для «{row.AccountName}» сохранён в зашифрованном файле."
                : $"The password for “{row.AccountName}” was saved to the encrypted vault.";
        }
        else
        {
            CredentialsStatus = result.Detail ?? (IsRussian ? "Не удалось сохранить пароль." : "Could not save the password.");
        }
        RefreshStoredCredentials(preserveStatus: true);
    }

    [RelayCommand]
    private void RemoveAccountCredential(AccountCredentialRowViewModel? row)
    {
        if (row is null || !row.HasSavedPassword) return;
        CredentialVaultWriteResult result = _credentialVault.Remove(row.Account.Account);
        CredentialsStatus = result.IsSuccess
            ? (IsRussian ? $"Пароль для «{row.AccountName}» удалён." : $"The password for “{row.AccountName}” was removed.")
            : result.Detail ?? (IsRussian ? "Не удалось удалить пароль." : "Could not remove the password.");
        RefreshStoredCredentials(preserveStatus: true);
    }

    [RelayCommand]
    private async Task SyncCredentialsAsync()
    {
        CredentialVaultLoadResult credentials = _credentialVault.Load();
        if (!credentials.IsReady || !credentials.Encrypted || credentials.Credentials.Count == 0)
        {
            CredentialsStatus = IsRussian
                ? "Сначала создайте зашифрованный файл и сохраните хотя бы один пароль."
                : "Create an encrypted vault and save at least one password first.";
            return;
        }

        if (_cloudProvider is not ICloudAuxiliaryFileProvider fileProvider)
        {
            CredentialsStatus = IsRussian ? "Выбранный cloud backend не поддерживает загрузку логинов." : "The selected cloud backend cannot upload credentials.";
            return;
        }

        string path = Path.Combine(_maFilesDirectory, "credentials.secure.json");
        string json;
        try
        {
            json = File.ReadAllText(path);
            JObject document = JObject.Parse(json);
            if (document.Value<bool?>("encrypted") != true || string.IsNullOrWhiteSpace(document.Value<string>("payload")))
            {
                CredentialsStatus = IsRussian ? "Файл логинов не зашифрован; отправка отменена." : "The credentials file is not encrypted; upload cancelled.";
                return;
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            CredentialsStatus = IsRussian ? "Не удалось прочитать файл логинов: " + exception.Message : "Could not read the credentials file: " + exception.Message;
            return;
        }

        CredentialsStatus = IsRussian ? "Отправка зашифрованных логинов..." : "Uploading encrypted credentials...";
        CloudFileResult result = await fileProvider.UploadFileAsync(CreateCloudSettings(), "credentials.secure.json", json);
        CredentialsStatus = result.Success
            ? (IsRussian ? "Зашифрованные логины отправлены в выбранное облако." : "Encrypted credentials were uploaded to the selected cloud.")
            : result.Message;
    }

    [RelayCommand]
    private void BeginAddCredential()
    {
        CredentialVaultLoadResult vault = _credentialVault.Load();
        if (!vault.IsReady)
        {
            CredentialsStatus = IsRussian
                ? "Сначала создайте или импортируйте зашифрованный файл логинов."
                : "Create or import an encrypted credentials vault first.";
            return;
        }
        PrepareCredentialEditor(SelectedAccount);
        IsCredentialEditorVisible = true;
    }

    [RelayCommand]
    private void CancelCredentialEditor()
    {
        CredentialEditorPassword = string.Empty;
        IsCredentialEditorVisible = false;
    }

    [RelayCommand]
    private async Task PickPlainCredentialImportAsync()
    {
        CredentialVaultLoadResult destination = _credentialVault.Load();
        if (!destination.IsReady)
        {
            CredentialsStatus = IsRussian
                ? "Сначала создайте зашифрованный файл логинов."
                : "Create the encrypted credentials vault first.";
            return;
        }

        string? path = await _accountFilePicker.PickPlainCredentialsFileAsync();
        if (string.IsNullOrWhiteSpace(path)) return;
        _plainCredentialImportSourcePath = path;
        IsPlainCredentialImportWarningVisible = true;
    }

    [RelayCommand]
    private void CancelPlainCredentialImport()
    {
        _plainCredentialImportSourcePath = string.Empty;
        IsPlainCredentialImportWarningVisible = false;
    }

    [RelayCommand]
    private void ConfirmPlainCredentialImport()
    {
        string sourcePath = _plainCredentialImportSourcePath;
        CancelPlainCredentialImport();
        CredentialVaultWriteResult result = _credentialVault.ImportPlaintext(sourcePath);
        CredentialsStatus = result.IsSuccess
            ? (IsRussian ? "Старые логины импортированы и сохранены в зашифрованном файле." : "Legacy credentials were imported into the encrypted vault.")
            : result.State == CredentialVaultState.Locked
                ? (IsRussian
                    ? "Не удалось расшифровать старый файл. Положите рядом с ним исходный cloud.secret.json и запускайте импорт под тем же пользователем Windows."
                    : "Could not decrypt the old file. Keep its original cloud.secret.json beside it and import under the same Windows user.")
                : result.Detail ?? (IsRussian ? "Не удалось импортировать старый файл." : "Could not import the legacy file.");
        RefreshStoredCredentials(preserveStatus: true);
        PrepareCredentialEditor(SelectedAccount);
    }

    [RelayCommand]
    private async Task BeginCredentialSetupAsync()
    {
        CredentialVaultLoadResult existing = _credentialVault.Load();
        if (existing.State != CredentialVaultState.Missing)
        {
            CredentialsStatus = IsRussian ? "Файл логинов уже существует." : "The credentials vault already exists.";
            return;
        }

        _credentialSetupCountdown?.Cancel();
        _credentialSetupCountdown?.Dispose();
        _credentialSetupCountdown = new CancellationTokenSource();
        GeneratedCredentialKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        CredentialKeyConfirmation = string.Empty;
        CanContinueCredentialSetup = false;
        IsCredentialKeyReentryVisible = false;
        IsCredentialFinalWarningVisible = false;
        IsCredentialSetupVisible = true;

        try
        {
            for (int remaining = 10; remaining > 0; remaining--)
            {
                CredentialSetupStatus = IsRussian
                    ? $"Сохраните ключ в безопасном месте. Продолжение станет доступно через {remaining} сек."
                    : $"Store this key safely. Continue will be available in {remaining} seconds.";
                await Task.Delay(1000, _credentialSetupCountdown.Token);
            }

            CanContinueCredentialSetup = true;
            IsCredentialKeyReentryVisible = true;
            CredentialSetupStatus = IsRussian
                ? "Введите сохранённый ключ ещё раз, чтобы подтвердить резервную копию."
                : "Re-enter the saved key to confirm that you have a backup.";
        }
        catch (OperationCanceledException)
        {
        }
    }

    [RelayCommand]
    private async Task CopyCredentialKeyAsync()
    {
        if (!string.IsNullOrEmpty(GeneratedCredentialKey))
        {
            await _clipboardService.SetTextAsync(GeneratedCredentialKey);
            CredentialSetupStatus = IsRussian ? "Ключ скопирован. Сохраните его вне SDA++." : "Key copied. Store it outside SDA++.";
        }
    }

    [RelayCommand]
    private async Task VerifyCredentialKeyAsync()
    {
        if (!CanContinueCredentialSetup) return;
        if (!CredentialKeysMatch(GeneratedCredentialKey, CredentialKeyConfirmation.Trim()))
        {
            ClearCredentialSetup();
            CredentialsStatus = IsRussian ? "Ключ введён неверно. Процедура сброшена." : "The key was incorrect. Setup was reset.";
            await BeginCredentialSetupAsync();
            return;
        }

        IsCredentialKeyReentryVisible = false;
        IsCredentialFinalWarningVisible = true;
        CredentialSetupStatus = IsRussian
            ? "Если вы потеряете этот ключ, сохранённые логины и пароли невозможно будет расшифровать или восстановить."
            : "If you lose this key, your saved login credentials cannot be decrypted or recovered.";
    }

    [RelayCommand]
    private void CreateCredentialVault()
    {
        if (!IsCredentialFinalWarningVisible || string.IsNullOrEmpty(GeneratedCredentialKey)) return;
        CredentialVaultWriteResult result = _credentialVault.CreateEncrypted(GeneratedCredentialKey);
        ClearCredentialSetup();
        CredentialsStatus = result.IsSuccess
            ? (IsRussian ? "Зашифрованный файл логинов создан." : "Encrypted credentials vault created.")
            : result.Detail ?? (IsRussian ? "Не удалось создать файл логинов." : "Could not create the credentials vault.");
        RefreshStoredCredentials(preserveStatus: true);
    }

    [RelayCommand]
    private void CancelCredentialSetup()
    {
        if (!CanContinueCredentialSetup) return;
        ClearCredentialSetup();
    }

    [RelayCommand]
    private async Task PickCredentialVaultAsync()
    {
        string? path = await _accountFilePicker.PickCredentialsFileAsync();
        if (string.IsNullOrWhiteSpace(path)) return;
        CredentialImportSourcePath = path;
        CredentialImportKey = string.Empty;
        CredentialImportStatus = string.Empty;
        IsCredentialImportVisible = true;
    }

    [RelayCommand]
    private void AskDeleteCredentialVault()
    {
        CredentialVaultLoadResult current = _credentialVault.Load();
        if (current.State == CredentialVaultState.Missing)
        {
            CredentialsStatus = IsRussian ? "Файл логинов ещё не создан." : "The credentials vault has not been created yet.";
            return;
        }
        IsCredentialVaultDeleteConfirmationVisible = true;
    }

    [RelayCommand]
    private void CancelDeleteCredentialVault() => IsCredentialVaultDeleteConfirmationVisible = false;

    [RelayCommand]
    private void ConfirmDeleteCredentialVault()
    {
        IsCredentialVaultDeleteConfirmationVisible = false;
        CredentialVaultWriteResult result = _credentialVault.DeleteVault();
        CredentialEditorUsername = SelectedAccount?.Name ?? string.Empty;
        CredentialEditorPassword = string.Empty;
        CredentialsStatus = result.IsSuccess
            ? (IsRussian ? "Зашифрованный файл логинов удалён." : "The encrypted credentials vault was deleted.")
            : result.Detail ?? (IsRussian ? "Не удалось удалить файл логинов." : "Could not delete the credentials vault.");
        RefreshStoredCredentials(preserveStatus: true);
    }

    [RelayCommand]
    private void CancelCredentialImport()
    {
        CleanupDownloadedCredentials();
        _resumeAutoLoginAfterCredentialImport = false;
        CredentialImportKey = string.Empty;
        CredentialImportSourcePath = string.Empty;
        CredentialImportStatus = string.Empty;
        IsCredentialImportVisible = false;
    }

    [RelayCommand]
    private void ConfirmCredentialImport()
    {
        CredentialVaultWriteResult result = _credentialVault.ImportEncrypted(CredentialImportSourcePath, CredentialImportKey);
        CredentialImportKey = string.Empty;
        if (!result.IsSuccess)
        {
            CredentialImportStatus = result.State == CredentialVaultState.Locked
                ? (IsRussian ? "Неверный ключ расшифрования." : "The decryption key is incorrect.")
                : result.Detail ?? (IsRussian ? "Импорт не выполнен." : "Import failed.");
            return;
        }

        CredentialImportSourcePath = string.Empty;
        IsCredentialImportVisible = false;
        CredentialsStatus = IsRussian ? "Зашифрованный файл логинов импортирован." : "Encrypted credentials vault imported.";
        RefreshStoredCredentials(preserveStatus: true);
        CleanupDownloadedCredentials();
        if (_resumeAutoLoginAfterCredentialImport)
        {
            _resumeAutoLoginAfterCredentialImport = false;
            _ = ShowAutoLoginAllPanelAsync();
        }
    }

    [RelayCommand]
    private async Task ShowAutoLoginAllPanelAsync()
    {
        if (IsCredentialDownloadBusy || IsCredentialImportVisible || IsCredentialCloudAuthVisible || IsAutoLoginAllBusy) return;
        CredentialVaultLoadResult credentials = _credentialVault.Load();
        if (credentials.State == CredentialVaultState.Missing)
        {
            ShowCredentials();
            await DownloadCredentialsAsync();
            if (IsCredentialImportVisible || IsCredentialCloudAuthVisible)
            {
                _resumeAutoLoginAfterCredentialImport = true;
                return;
            }
        }
        VaultEncryptionStatus vaultProtection = _vaultEncryptionService.Inspect(_maFilesDirectory);
        AutoLoginAllVaultPassword = string.Empty;
        CanRunAutoLoginAll = credentials.IsReady && credentials.Credentials.Count > 0;
        AutoLoginAllNeedsVaultPassword = CanRunAutoLoginAll && vaultProtection.IsEncrypted;
        AutoLoginAllStatus = credentials.State switch
        {
            CredentialVaultState.Ready when credentials.Credentials.Count > 0 => IsRussian
                ? $"Найдено сохранённых логинов: {credentials.Credentials.Count}. Будут обработаны только истёкшие сессии."
                : $"Saved logins found: {credentials.Credentials.Count}. Only expired sessions will be processed.",
            CredentialVaultState.Ready => IsRussian
                ? "Файл логинов создан, но в нём пока нет сохранённых аккаунтов. Добавьте логины в разделе «Логины»."
                : "The credentials vault exists, but it has no saved accounts yet. Add credentials on the Credentials page.",
            CredentialVaultState.Missing => IsRussian
                ? "Облачные логины не восстановлены. " + CredentialsStatus + " Проверьте настройки облака или импортируйте существующий файл в разделе «Логины»."
                : "Cloud credentials could not be restored. " + CredentialsStatus + " Check cloud settings or import your existing file on the Credentials page.",
            CredentialVaultState.Locked => IsRussian ? "Зашифрованный файл логинов заблокирован для этого пользователя Windows." : "The encrypted credentials vault is locked for this Windows user.",
            _ => credentials.Detail ?? (IsRussian ? "Не удалось открыть файл логинов." : "Could not open the credentials vault.")
        };
        IsAutoLoginAllPanelVisible = true;
    }

    [RelayCommand]
    private void CancelAutoLoginAll()
    {
        if (IsAutoLoginAllBusy) return;
        AutoLoginAllVaultPassword = string.Empty;
        AutoLoginAllStatus = string.Empty;
        CanRunAutoLoginAll = false;
        AutoLoginAllNeedsVaultPassword = false;
        IsAutoLoginAllPanelVisible = false;
    }

    [RelayCommand]
    private void ConfigureCredentialsFromAutoLogin()
    {
        CancelAutoLoginAll();
        ShowCredentials();
    }

    [RelayCommand]
    private async Task ConfirmAutoLoginAllAsync()
    {
        if (IsAutoLoginAllBusy) return;
        CredentialVaultLoadResult credentials = _credentialVault.Load();
        if (!credentials.IsReady || credentials.Credentials.Count == 0)
        {
            AutoLoginAllStatus = IsRussian
                ? "Нет сохранённых логинов. Откройте раздел «Логины» и настройте их сначала."
                : "No saved credentials are available. Open Credentials and set them up first.";
            return;
        }

        string vaultPassword = AutoLoginAllNeedsVaultPassword ? AutoLoginAllVaultPassword : string.Empty;
        VaultLoadResult vaultValidation = _vaultReader.Load(_maFilesDirectory, vaultPassword);
        if (vaultValidation.State is VaultLoadState.Locked or VaultLoadState.InvalidPassword or VaultLoadState.Corrupt or VaultLoadState.Missing)
        {
            AutoLoginAllVaultPassword = string.Empty;
            AutoLoginAllStatus = vaultValidation.State == VaultLoadState.InvalidPassword
                ? Text.WrongPasswordDetail
                : vaultValidation.Detail ?? Text.VaultLockedDetail;
            return;
        }

        IsAutoLoginAllBusy = true;
        int restored = 0;
        int skipped = 0;
        int failed = 0;
        try
        {
            foreach (AccountItemViewModel item in _allAccounts)
            {
                SteamAuth.SessionData? current = item.Account.GuardAccount.Session;
                bool needsLogin = current is null || current.IsAccessTokenExpired() || current.IsRefreshTokenExpired();
                if (!needsLogin)
                {
                    skipped++;
                    continue;
                }
                if (!_credentialVault.TryGet(item.Account, out StoredCredential? credential) || credential is null)
                {
                    failed++;
                    continue;
                }

                AutoLoginAllStatus = (IsRussian ? "Вход: " : "Signing in: ") + item.Name;
                SteamLoginResult login = await _sessionLoginService.LoginAsync(
                    item.Account.GuardAccount,
                    credential.Username,
                    credential.Password);
                if (!login.IsSuccess)
                {
                    failed++;
                    continue;
                }

                SteamAuth.SessionData? previous = item.Account.GuardAccount.Session;
                item.Account.GuardAccount.Session = login.Session!;
                VaultWriteResult write = _vaultAccountWriter.SaveSession(_maFilesDirectory, item.Account, vaultPassword);
                if (!write.IsSuccess)
                {
                    item.Account.GuardAccount.Session = previous;
                    failed++;
                    continue;
                }
                item.RefreshSessionState();
                restored++;
            }

            AutoLoginAllStatus = IsRussian
                ? $"Готово: восстановлено {restored}, пропущено активных {skipped}, ошибок/нет логина {failed}."
                : $"Complete: restored {restored}, active skipped {skipped}, failed/missing credentials {failed}.";
            StatusMessage = AutoLoginAllStatus;
        }
        finally
        {
            vaultPassword = string.Empty;
            AutoLoginAllVaultPassword = string.Empty;
            IsAutoLoginAllBusy = false;
        }
    }

    [RelayCommand]
    private async Task RefreshMonitoringAsync()
    {
        if (IsMonitoringBusy)
        {
            return;
        }

        IsMonitoringBusy = true;
        MonitoringStatus = IsRussian ? "Получение данных Steam Community..." : "Loading Steam Community data...";
        try
        {
            using var gate = new SemaphoreSlim(4);
            Task<AccountMonitoringSnapshot>[] tasks = _allAccounts.Select(async item =>
            {
                await gate.WaitAsync();
                try
                {
                    return await _accountMonitoringService.LoadSnapshotAsync(item.Account, forceRefresh: true);
                }
                finally
                {
                    gate.Release();
                }
            }).ToArray();
            AccountMonitoringSnapshot[] snapshots = await Task.WhenAll(tasks);
            MonitoringAccounts.Clear();
            foreach (AccountMonitoringSnapshot snapshot in snapshots.OrderBy(item => item.Account.Name, StringComparer.OrdinalIgnoreCase))
            {
                MonitoringAccounts.Add(new MonitoringItemViewModel(snapshot, (AppLanguage)SelectedLanguageIndex));
            }
            MonitoringStatus = IsRussian
                ? $"Проверено аккаунтов: {snapshots.Length}"
                : $"Accounts checked: {snapshots.Length}";
        }
        catch (Exception exception)
        {
            MonitoringStatus = (IsRussian ? "Мониторинг не завершён: " : "Monitoring failed: ") + exception.Message;
        }
        finally
        {
            IsMonitoringBusy = false;
        }
    }

    [RelayCommand]
    private async Task ShowConfirmationsAsync()
    {
        SetPage(settings: false, cloud: false, qr: false, confirmations: true);
        await RefreshConfirmationsAsync();
    }

    [RelayCommand]
    private async Task RefreshConfirmationsAsync()
    {
        if (IsConfirmationBusy)
        {
            return;
        }

        IsConfirmationBusy = true;
        IsConfirmationActionPending = false;
        _pendingConfirmation = null;
        ConfirmationsStatus = Text.LoadingConfirmations;
        _confirmationRefreshCancellation?.Cancel();
        _confirmationRefreshCancellation?.Dispose();
        _confirmationRefreshCancellation = new CancellationTokenSource();
        CancellationToken cancellationToken = _confirmationRefreshCancellation.Token;
        try
        {
            ConfirmationLoadResult result = await _confirmationService.LoadAsync(
                _allAccounts.Select(item => item.Account),
                cancellationToken,
                SelectedAccount?.SteamId,
                forceRefresh: true);
            ApplyConfirmationResult(result);
            if (result.DeferredAccounts > 0)
            {
                _ = ContinueConfirmationRefreshAsync(cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch
        {
            ConfirmationsStatus = Text.ConfirmationsLoadFailed;
        }
        finally
        {
            IsConfirmationBusy = false;
        }
    }

    private async Task ContinueConfirmationRefreshAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested && IsConfirmationsVisible)
            {
                await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
                ConfirmationLoadResult result = await _confirmationService.LoadAsync(
                    _allAccounts.Select(item => item.Account),
                    cancellationToken,
                    SelectedAccount?.SteamId);
                ApplyConfirmationResult(result);
                if (result.DeferredAccounts == 0) break;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
    }

    private void ApplyConfirmationResult(ConfirmationLoadResult result)
    {
        var resultKeys = result.Items.Select(item => $"{item.SteamId}:{item.ConfirmationId}").ToHashSet(StringComparer.Ordinal);
        foreach (ConfirmationItemViewModel existing in Confirmations
                     .Where(item => !resultKeys.Contains($"{item.SteamId}:{item.Confirmation.ConfirmationId}"))
                     .ToArray())
        {
            Confirmations.Remove(existing);
        }
        var existingKeys = Confirmations
            .Select(item => $"{item.SteamId}:{item.Confirmation.ConfirmationId}")
            .ToHashSet(StringComparer.Ordinal);
        foreach (AccountConfirmation item in result.Items)
        {
            string key = $"{item.SteamId}:{item.ConfirmationId}";
            if (!existingKeys.Add(key)) continue;
            var viewModel = new ConfirmationItemViewModel(item, SelectedLanguageIndex == (int)AppLanguage.Russian);
            Confirmations.Add(viewModel);
            _ = viewModel.LoadAvatarsAsync();
        }

        ConfirmationsStatus = result.Items.Count == 0
            ? string.Format(Text.NoConfirmationsFormat, result.AccountsChecked, result.ExpiredAccounts, result.FailedAccounts)
            : string.Format(Text.ConfirmationsLoadedFormat, result.Items.Count, result.AccountsChecked, result.ExpiredAccounts, result.FailedAccounts);
        if (result.DeferredAccounts > 0)
        {
            ConfirmationsStatus += IsRussian
                ? $" Фоновая проверка: осталось {result.DeferredAccounts}."
                : $" Background scan: {result.DeferredAccounts} remaining.";
        }
        if (result.RefreshedAccounts > 0)
        {
            ConfirmationsStatus += IsRussian
                ? $" Обновлено сессий: {result.RefreshedAccounts}."
                : $" Sessions refreshed: {result.RefreshedAccounts}.";
        }
        if (result.Items.Count == 0 && result.DeferredAccounts == 0 && result.FailureDetails is { Count: > 0 })
        {
            ConfirmationsStatus += Environment.NewLine + string.Join(Environment.NewLine, result.FailureDetails.Take(4));
        }
    }

    private void UpdateConfirmationPollingTimer()
    {
        _confirmationPollingTimer.Interval = TimeSpan.FromSeconds(Math.Clamp(ConfirmationCheckIntervalSeconds, 5, 3600));
        if (PeriodicConfirmationChecking)
        {
            _confirmationPollingTimer.Start();
        }
        else
        {
            _confirmationPollingTimer.Stop();
            ConfirmationPollingStatus = string.Empty;
        }
    }

    private async Task PollConfirmationsAsync()
    {
        if (_confirmationPollingBusy || IsConfirmationBusy || !PeriodicConfirmationChecking)
        {
            return;
        }

        VaultAccount[] accounts = CheckAllAccountsForConfirmations
            ? _allAccounts.Select(item => item.Account).ToArray()
            : SelectedAccount is null ? [] : [SelectedAccount.Account];
        if (accounts.Length == 0)
        {
            ConfirmationPollingStatus = IsRussian ? "Нет аккаунта для проверки." : "No account is available to check.";
            return;
        }

        _confirmationPollingBusy = true;
        try
        {
            ConfirmationLoadResult result = await _confirmationService.LoadAsync(
                accounts,
                preferredSteamId: SelectedAccount?.SteamId,
                forceRefresh: true);
            ApplyConfirmationResult(result);
            int accepted = await AutoProcessConfirmationsAsync(result.Items);
            ConfirmationPollingStatus = IsRussian
                ? $"Проверено: {result.AccountsChecked}; ожидает: {Math.Max(0, result.Items.Count - accepted)}; принято автоматически: {accepted}."
                : $"Checked: {result.AccountsChecked}; pending: {Math.Max(0, result.Items.Count - accepted)}; auto-accepted: {accepted}.";
            if (result.Items.Count > accepted)
            {
                StatusMessage = IsRussian
                    ? $"Новых подтверждений: {result.Items.Count - accepted}"
                    : $"New confirmations: {result.Items.Count - accepted}";
            }
        }
        catch
        {
            ConfirmationPollingStatus = IsRussian
                ? "Фоновая проверка подтверждений не удалась."
                : "Background confirmation check failed.";
        }
        finally
        {
            _confirmationPollingBusy = false;
        }
    }

    private async Task<int> AutoProcessConfirmationsAsync(IReadOnlyList<AccountConfirmation> confirmations)
    {
        int accepted = 0;
        DateTimeOffset retryBefore = DateTimeOffset.UtcNow - TimeSpan.FromMinutes(1);
        foreach (AccountConfirmation confirmation in confirmations)
        {
            bool market = AutoConfirmMarketTransactions &&
                confirmation.Type == SteamAuth.Confirmation.EMobileConfirmationType.MarketListing;
            bool possibleGift = AutoConfirmGiftTrades &&
                confirmation.Type == SteamAuth.Confirmation.EMobileConfirmationType.Trade;
            if (!market && !possibleGift) continue;

            string key = $"{confirmation.SteamId}:{confirmation.ConfirmationId}";
            if (_automaticConfirmationAttempts.TryGetValue(key, out DateTimeOffset attemptedAt) && attemptedAt > retryBefore)
            {
                continue;
            }
            _automaticConfirmationAttempts[key] = DateTimeOffset.UtcNow;

            if (possibleGift)
            {
                ConfirmationDetailsResult details = await _confirmationService.LoadDetailsAsync(confirmation);
                if (!ConfirmationAutomationPolicy.IsVerifiedGiftTrade(details)) continue;
            }

            ConfirmationActionResult result = await _confirmationService.ApplyAsync(confirmation, ConfirmationDecision.Accept);
            if (!result.Success) continue;
            accepted++;
            ConfirmationItemViewModel? item = Confirmations.FirstOrDefault(candidate =>
                candidate.SteamId == confirmation.SteamId &&
                candidate.Confirmation.ConfirmationId == confirmation.ConfirmationId);
            if (item is not null) Confirmations.Remove(item);
            _automaticConfirmationAttempts.Remove(key);
        }

        var active = confirmations.Select(item => $"{item.SteamId}:{item.ConfirmationId}").ToHashSet(StringComparer.Ordinal);
        foreach (string stale in _automaticConfirmationAttempts.Keys.Where(key => !active.Contains(key)).ToArray())
        {
            _automaticConfirmationAttempts.Remove(stale);
        }
        return accepted;
    }

    [RelayCommand]
    private void QueueAcceptConfirmation(ConfirmationItemViewModel? item) =>
        QueueConfirmationAction(item, ConfirmationDecision.Accept);

    [RelayCommand]
    private void QueueRejectConfirmation(ConfirmationItemViewModel? item) =>
        QueueConfirmationAction(item, ConfirmationDecision.Reject);

    [RelayCommand]
    private async Task ToggleConfirmationDetailsAsync(ConfirmationItemViewModel? item)
    {
        if (item is null || item.IsDetailsBusy) return;
        if (!string.IsNullOrWhiteSpace(item.DetailsText))
        {
            item.ToggleLoadedDetails();
            return;
        }

        item.SetDetailsLoading(true);
        ConfirmationDetailsResult result = await _confirmationService.LoadDetailsAsync(item.Confirmation);
        item.SetDetails(result);
    }

    [RelayCommand]
    private void CancelConfirmationAction()
    {
        _pendingConfirmation = null;
        IsConfirmationActionPending = false;
        ConfirmationActionPrompt = string.Empty;
    }

    [RelayCommand]
    private async Task ConfirmConfirmationActionAsync()
    {
        ConfirmationItemViewModel? item = _pendingConfirmation;
        if (item is null || IsConfirmationBusy)
        {
            return;
        }

        IsConfirmationBusy = true;
        IsConfirmationActionPending = false;
        try
        {
            ConfirmationActionResult result = await _confirmationService.ApplyAsync(item.Confirmation, _pendingConfirmationDecision);
            if (result.Success)
            {
                Confirmations.Remove(item);
                ConfirmationsStatus = Text.ConfirmationActionSucceeded;
            }
            else
            {
                ConfirmationsStatus = Text.ConfirmationActionFailed;
            }
        }
        catch
        {
            ConfirmationsStatus = Text.ConfirmationActionFailed;
        }
        finally
        {
            _pendingConfirmation = null;
            IsConfirmationBusy = false;
        }
    }

    [RelayCommand]
    private async Task ScanQrAsync()
    {
        if (IsQrBusy)
        {
            return;
        }

        if (SelectedAccount is null)
        {
            SetQrState(QrUiState.NoAccount);
            return;
        }

        IsQrBusy = true;
        try
        {
            SetQrState(QrUiState.Scanning);
            string? qrText = await _screenQrScanner.ScanAsync(
                (QrCaptureMode)Math.Clamp(SelectedQrCaptureModeIndex, 0, 2),
                Math.Clamp(QrCursorAreaSize, 250, 2000));
            if (string.IsNullOrWhiteSpace(qrText))
            {
                SetQrState(QrUiState.NotFound);
                return;
            }

            if (!SteamQrParser.TryParse(qrText, out _))
            {
                SetQrState(QrUiState.Unsupported);
                return;
            }

            SetQrState(QrUiState.Approving);
            SteamQrApprovalResult result = await _qrApprovalService.ApproveAsync(SelectedAccount.Account, qrText);
            SetQrState(result.Success ? QrUiState.Approved : result.Failure switch
            {
                SteamQrApprovalFailure.MissingAccount => QrUiState.NoAccount,
                SteamQrApprovalFailure.MissingSession => QrUiState.MissingSession,
                SteamQrApprovalFailure.ExpiredSession => QrUiState.ExpiredSession,
                SteamQrApprovalFailure.MissingSecret => QrUiState.MissingSecret,
                SteamQrApprovalFailure.InvalidQr => QrUiState.Unsupported,
                _ => QrUiState.Rejected
            });
        }
        catch (PlatformNotSupportedException)
        {
            SetQrState(QrUiState.UnsupportedPlatform);
        }
        catch (OperationCanceledException)
        {
            SetQrState(QrUiState.Ready);
        }
        catch
        {
            SetQrState(QrUiState.Rejected);
        }
        finally
        {
            IsQrBusy = false;
        }
    }

    [RelayCommand]
    private void ReloadAccounts() => ApplyVaultResult(_vaultReader.Load(_maFilesDirectory));

    [RelayCommand]
    private void UnlockVault()
    {
        string password = VaultPassword;
        try
        {
            ApplyVaultResult(_vaultReader.Load(_maFilesDirectory, password));
        }
        finally
        {
            VaultPassword = string.Empty;
        }
    }

    [RelayCommand]
    private void ShowLoginPanel()
    {
        if (SelectedAccount is null)
        {
            StatusMessage = Text.LoginMissingAccount;
            return;
        }

        HasStoredLoginCredential = _credentialVault.TryGet(
            SelectedAccount.Account,
            out StoredCredential? storedCredential);
        LoginUsername = storedCredential?.Username ?? SelectedAccount.Name;
        LoginPassword = storedCredential?.Password ?? string.Empty;
        LoginVaultPassword = string.Empty;
        LoginStatus = HasStoredLoginCredential
            ? (IsRussian
                ? "Сохранённые данные найдены. Пароль подставлен из зашифрованного хранилища."
                : "Saved credentials found. The password was loaded from the encrypted vault.")
            : (IsRussian
                ? "Для этого аккаунта нет сохранённых данных. Введите логин и пароль вручную."
                : "No saved credentials were found for this account. Enter the login and password manually.");
        StoreLoginCredential = HasStoredLoginCredential;
        IsLoginEmailCodeRequested = false;
        LoginEmailCode = string.Empty;
        IsLoginPanelVisible = true;
    }

    [RelayCommand]
    private void CancelLogin()
    {
        _loginCancellation?.Cancel();
        _emailCodeCompletion?.TrySetResult(null);
        ClearLoginSecrets();
        LoginStatus = string.Empty;
        IsLoginEmailCodeRequested = false;
        IsLoginPanelVisible = false;
    }

    [RelayCommand]
    private void SubmitLoginEmailCode()
    {
        string code = LoginEmailCode.Trim();
        if (!string.IsNullOrEmpty(code))
        {
            _emailCodeCompletion?.TrySetResult(code);
        }
    }

    [RelayCommand]
    private async Task LoginAgainAsync()
    {
        AccountItemViewModel? selected = SelectedAccount;
        if (selected is null || IsLoginBusy)
        {
            LoginStatus = Text.LoginMissingAccount;
            return;
        }

        string username = LoginUsername.Trim();
        string steamPassword = LoginPassword;
        string vaultPassword = LoginVaultPassword;
        if ((string.IsNullOrWhiteSpace(username) || string.IsNullOrEmpty(steamPassword)) &&
            _credentialVault.TryGet(selected.Account, out StoredCredential? storedCredential) &&
            storedCredential is not null)
        {
            username = storedCredential.Username;
            steamPassword = storedCredential.Password;
            HasStoredLoginCredential = true;
        }
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrEmpty(steamPassword))
        {
            LoginStatus = Text.LoginFailed;
            ClearLoginSecrets();
            return;
        }

        IsLoginBusy = true;
        LoginStatus = Text.LoginInProgress;
        _loginCancellation = new CancellationTokenSource();
        try
        {
            SteamLoginResult login = await _sessionLoginService.LoginAsync(
                selected.Account.GuardAccount,
                username,
                steamPassword,
                RequestEmailCodeAsync,
                _loginCancellation.Token);
            if (!login.IsSuccess)
            {
                LoginStatus = login.State == SteamLoginState.EmailCodeRequired
                    ? Text.LoginEmailCodeRequired
                    : login.Detail ?? Text.LoginFailed;
                return;
            }

            SteamAuth.SessionData? previousSession = selected.Account.GuardAccount.Session;
            selected.Account.GuardAccount.Session = login.Session!;
            VaultWriteResult write = _vaultAccountWriter.SaveSession(_maFilesDirectory, selected.Account, vaultPassword);
            if (!write.IsSuccess)
            {
                selected.Account.GuardAccount.Session = previousSession;
                LoginStatus = write.State switch
                {
                    VaultWriteState.Locked => Text.VaultLockedDetail,
                    VaultWriteState.InvalidPassword => Text.WrongPasswordDetail,
                    _ => write.Detail ?? Text.LoginFailed
                };
                return;
            }

            selected.RefreshSessionState();
            if (StoreLoginCredential)
            {
                CredentialVaultWriteResult credentialWrite = _credentialVault.Save(selected.Account, username, steamPassword);
                if (!credentialWrite.IsSuccess)
                {
                    LoginStatus = Text.LoginSucceeded + " " + (credentialWrite.Detail ??
                        (IsRussian ? "Пароль не сохранён." : "Password was not saved."));
                    StatusMessage = LoginStatus;
                    IsLoginPanelVisible = false;
                    return;
                }
            }
            LoginStatus = Text.LoginSucceeded;
            StatusMessage = Text.LoginSucceeded;
            IsLoginPanelVisible = false;
        }
        catch
        {
            LoginStatus = Text.LoginFailed;
        }
        finally
        {
            _emailCodeCompletion?.TrySetResult(null);
            _emailCodeCompletion = null;
            _loginCancellation?.Dispose();
            _loginCancellation = null;
            IsLoginEmailCodeRequested = false;
            ClearLoginSecrets();
            IsLoginBusy = false;
        }
    }

    [RelayCommand]
    private void ShowEndSessionsPanel()
    {
        if (SelectedAccount is null)
        {
            StatusMessage = Text.LoginMissingAccount;
            return;
        }

        EndSessionsPrompt = IsRussian
            ? $"Это завершит все Steam-сессии аккаунта «{SelectedAccount.Name}» на всех устройствах, включая текущую сессию SDA++. Продолжить?"
            : $"This will terminate every Steam session for “{SelectedAccount.Name}” on every device, including the current SDA++ session. Continue?";
        EndSessionsVaultPassword = string.Empty;
        EndSessionsStatus = string.Empty;
        IsEndSessionsPanelVisible = true;
    }

    [RelayCommand]
    private async Task ShowSteamSessionsAsync()
    {
        if (SelectedAccount is null)
        {
            StatusMessage = Text.LoginMissingAccount;
            return;
        }

        IsSteamSessionsPanelVisible = true;
        await RefreshSteamSessionsAsync();
    }

    [RelayCommand]
    private void CloseSteamSessions()
    {
        if (IsSteamSessionsBusy)
        {
            return;
        }

        IsSteamSessionsPanelVisible = false;
        SteamSessionsStatus = string.Empty;
        ActiveSteamSessions.Clear();
    }

    [RelayCommand]
    private async Task RefreshSteamSessionsAsync()
    {
        AccountItemViewModel? selected = SelectedAccount;
        if (selected is null || IsSteamSessionsBusy)
        {
            return;
        }

        IsSteamSessionsBusy = true;
        SteamSessionsStatus = IsRussian ? "Получение сессий Steam…" : "Loading Steam sessions…";
        try
        {
            SteamSessionListResult result = await _sessionTerminationService.GetActiveSessionsAsync(selected.Account.GuardAccount);
            ActiveSteamSessions.Clear();
            if (!result.IsSuccess)
            {
                SteamSessionsStatus = result.Detail ?? (IsRussian
                    ? "Steam не вернул список активных сессий. Возможно, access token истёк."
                    : "Steam did not return active sessions. The access token may have expired.");
                return;
            }

            foreach (SteamActiveSession session in result.Sessions ?? [])
            {
                ActiveSteamSessions.Add(new SteamSessionItemViewModel(session, selected.Name, IsRussian));
            }

            SteamSessionsStatus = ActiveSteamSessions.Count == 0
                ? (IsRussian ? "Активных Steam-сессий не найдено." : "No active Steam sessions were found.")
                : string.Format(IsRussian ? "Найдено сессий: {0}" : "Sessions found: {0}", ActiveSteamSessions.Count);
        }
        catch
        {
            SteamSessionsStatus = IsRussian ? "Не удалось получить активные сессии." : "Could not load active sessions.";
        }
        finally
        {
            IsSteamSessionsBusy = false;
        }
    }

    [RelayCommand]
    private void CancelEndSessions()
    {
        if (IsEndSessionsBusy)
        {
            return;
        }

        EndSessionsVaultPassword = string.Empty;
        EndSessionsStatus = string.Empty;
        IsEndSessionsPanelVisible = false;
    }

    [RelayCommand]
    private async Task ConfirmEndSessionsAsync()
    {
        AccountItemViewModel? selected = SelectedAccount;
        if (selected is null || IsEndSessionsBusy)
        {
            return;
        }

        string vaultPassword = EndSessionsVaultPassword;
        VaultLoadResult validation = _vaultReader.Load(_maFilesDirectory, vaultPassword);
        if (!validation.IsReady)
        {
            EndSessionsStatus = validation.State == VaultLoadState.InvalidPassword
                ? Text.WrongPasswordDetail
                : validation.Detail ?? Text.VaultLockedDetail;
            EndSessionsVaultPassword = string.Empty;
            return;
        }

        IsEndSessionsBusy = true;
        EndSessionsStatus = Text.EndingSessions;
        try
        {
            SteamSessionTerminationResult result = await _sessionTerminationService.TerminateAllAsync(selected.Account.GuardAccount);
            if (!result.IsSuccess)
            {
                EndSessionsStatus = result.State is SteamSessionTerminationState.MissingSession or SteamSessionTerminationState.ExpiredSession
                    ? Text.SessionsMissing
                    : result.Detail ?? Text.SessionsEndFailed;
                return;
            }

            SteamAuth.SessionData? session = selected.Account.GuardAccount.Session;
            string? accessToken = session?.AccessToken;
            string? refreshToken = session?.RefreshToken;
            string? sessionId = session?.SessionID;
            if (session is not null)
            {
                session.AccessToken = null;
                session.RefreshToken = null;
                session.SessionID = null;
            }

            VaultWriteResult write = _vaultAccountWriter.SaveSession(_maFilesDirectory, selected.Account, vaultPassword);
            if (!write.IsSuccess)
            {
                if (session is not null)
                {
                    session.AccessToken = accessToken;
                    session.RefreshToken = refreshToken;
                    session.SessionID = sessionId;
                }

                EndSessionsStatus = Text.SessionsEndedSaveFailed;
                return;
            }

            selected.RefreshSessionState();
            StatusMessage = Text.SessionsEnded;
            IsEndSessionsPanelVisible = false;
            IsSteamSessionsPanelVisible = false;
            ActiveSteamSessions.Clear();
        }
        catch
        {
            EndSessionsStatus = Text.SessionsEndFailed;
        }
        finally
        {
            EndSessionsVaultPassword = string.Empty;
            IsEndSessionsBusy = false;
        }
    }

    [RelayCommand]
    private void SavePreferences()
    {
        if (!HotkeyGestureParser.TryParse(QrToggleHotkey, out HotkeyGesture? toggleGesture) ||
            !HotkeyGestureParser.TryParse(QrScanHotkey, out HotkeyGesture? scanGesture) ||
            !HotkeyGestureParser.TryParse(PreviousAccountHotkey, out HotkeyGesture? previousGesture) ||
            !HotkeyGestureParser.TryParse(NextAccountHotkey, out HotkeyGesture? nextGesture) ||
            !HotkeyGestureParser.TryParse(AutoLoginAllHotkey, out HotkeyGesture? autoLoginGesture) ||
            !HotkeyGestureParser.TryParse(ConfirmationsHotkey, out HotkeyGesture? confirmationsGesture))
        {
            SetHotkeyState(HotkeyUiState.Invalid);
            return;
        }
        HotkeyGesture[] gestures = [toggleGesture!, scanGesture!, previousGesture!, nextGesture!, autoLoginGesture!, confirmationsGesture!];
        if (gestures.Distinct().Count() != gestures.Length)
        {
            SetHotkeyState(HotkeyUiState.Duplicate);
            return;
        }

        QrToggleHotkey = toggleGesture!.ToString();
        QrScanHotkey = scanGesture!.ToString();
        PreviousAccountHotkey = previousGesture!.ToString();
        NextAccountHotkey = nextGesture!.ToString();
        AutoLoginAllHotkey = autoLoginGesture!.ToString();
        ConfirmationsHotkey = confirmationsGesture!.ToString();
        _preferenceStore.Save(CreatePreferences());
        ConfigureHotkeys();
        UpdateConfirmationPollingTimer();
        SettingsNotice = Text.PreferencesSaved;
        ApplyAccountFilter();
    }

    [RelayCommand]
    private async Task CopyCodeAsync()
    {
        if (SelectedAccount is not null && SelectedAccount.Code != "-----")
        {
            await _clipboardService.SetTextAsync(SelectedAccount.Code);
        }
    }

    [RelayCommand]
    private async Task CopySteamIdAsync()
    {
        if (SelectedAccount is not null && SelectedAccount.SteamId != "SteamID unavailable")
        {
            await _clipboardService.SetTextAsync(SelectedAccount.SteamId);
        }
    }

    [RelayCommand]
    private void ToggleFavorite()
    {
        AccountItemViewModel? selected = SelectedAccount;
        if (selected is null)
        {
            return;
        }

        bool favorite = !selected.IsFavorite;
        VaultWriteResult result = _vaultAccountWriter.SetFavorite(_maFilesDirectory, selected.Account, favorite);
        if (!result.IsSuccess)
        {
            StatusMessage = result.Detail ?? Text.FavoriteUpdateFailed;
            return;
        }

        selected.SetFavorite(favorite);
        StatusMessage = favorite ? Text.FavoriteAdded : Text.FavoriteRemoved;
        ApplyAccountFilter();
    }

    [RelayCommand]
    private async Task TestCloudAsync()
    {
        IsCloudBusy = true;
        CloudPreviewSummary = string.Empty;
        try
        {
            CloudConnectionResult result = await _cloudProvider.TestConnectionAsync(CreateCloudSettings());
            CloudStatus = result.Success ? Text.CloudConnected : result.Message;
        }
        finally
        {
            ClearCloudSecrets();
            IsCloudBusy = false;
        }
    }

    [RelayCommand]
    private async Task AuthenticateCloudAccountAsync()
    {
        if (IsCloudBusy || _cloudProvider is not ICloudAuthenticationProvider authentication) return;
        CloudConnectionSettings settings = CreateCloudSettings();
        if (!authentication.RequiresInteractiveAuthentication(settings.Provider)) return;
        IsCloudBusy = true;
        CloudStatus = IsRussian ? "Запуск защищённой авторизации..." : "Starting secure authorization...";
        try
        {
            CloudConnectionResult result = await authentication.AuthenticateAsync(settings, message =>
                Dispatcher.UIThread.Post(() => CloudStatus = message));
            CloudStatus = result.Message;
        }
        finally
        {
            IsCloudBusy = false;
        }
    }

    [RelayCommand]
    private async Task PushCloudAsync()
    {
        if (IsCloudBusy) return;
        CloudPushPackageResult prepared = new CloudPushPackageFactory().Create(_maFilesDirectory);
        if (!prepared.Success || prepared.Package is null)
        {
            CloudStatus = prepared.Message;
            return;
        }
        CloudPushPackage package = AttachEncryptedCredentials(prepared.Package);
        IsCloudBusy = true;
        CloudStatus = IsRussian ? "Отправка зашифрованного vault..." : "Uploading encrypted vault...";
        try
        {
            CloudPushResult result = await _cloudProvider.PushAsync(CreateCloudSettings(), package);
            CloudStatus = result.Message;
        }
        finally
        {
            ClearCloudSecrets();
            IsCloudBusy = false;
        }
    }

    [RelayCommand]
    private async Task PreviewCloudPullAsync()
    {
        IsCloudBusy = true;
        try
        {
            CloudPullPreview result = await _cloudProvider.PreviewPullAsync(CreateCloudSettings());
            CloudStatus = result.Success ? Text.CloudConnected : result.Message;
            CloudPreviewSummary = result.Success
                ? string.Format(
                    Text.CloudPreviewFoundFormat,
                    result.AccountCount,
                    result.IsEncrypted ? Text.Yes : Text.No,
                    result.ManifestFound ? Text.Yes : Text.FallbackListing)
                : string.Empty;
        }
        finally
        {
            ClearCloudSecrets();
            IsCloudBusy = false;
        }
    }

    [RelayCommand]
    private async Task PrepareCloudImportAsync()
    {
        IsCloudBusy = true;
        IsImportConfirmationVisible = false;
        _pendingCloudPackage = null;
        try
        {
            CloudPullPackageResult result = await _cloudProvider.PreparePullAsync(CreateCloudSettings());
            CloudStatus = result.Message;
            if (result.Success && result.Package is not null)
            {
                _pendingCloudPackage = result.Package;
                ImportConfirmationText = string.Format(Text.ImportWarningFormat, result.Package.AccountFiles.Count);
                IsImportConfirmationVisible = true;
            }
        }
        finally
        {
            ClearCloudSecrets();
            IsCloudBusy = false;
        }
    }

    [RelayCommand]
    private void ConfirmCloudImport()
    {
        if (_pendingCloudPackage is null)
        {
            return;
        }

        VaultImportResult result = _vaultImportService.Commit(_pendingCloudPackage, _maFilesDirectory);
        CloudStatus = result.Success
            ? string.Format(Text.ImportCompleteFormat, result.AccountCount, result.BackupDirectory)
            : result.Message;
        _pendingCloudPackage = null;
        IsImportConfirmationVisible = false;
        if (result.Success)
        {
            ApplyVaultResult(_vaultReader.Load(_maFilesDirectory));
        }
    }

    [RelayCommand]
    private void CancelCloudImport()
    {
        _pendingCloudPackage = null;
        IsImportConfirmationVisible = false;
    }

    partial void OnSelectedAccountChanged(AccountItemViewModel? value)
    {
        OnPropertyChanged(nameof(HasSelectedAccount));
        OnPropertyChanged(nameof(SelectedAccountName));
        RefreshCodes();
        if (IsCredentialsVisible) PrepareCredentialEditor(value);
    }

    partial void OnIsCredentialEditorVisibleChanged(bool value) =>
        OnPropertyChanged(nameof(IsCredentialEmptyStateVisible));

    partial void OnHasStoredLoginCredentialChanged(bool value) =>
        OnPropertyChanged(nameof(LoginActionLabel));

    partial void OnSearchTextChanged(string value) => ApplyAccountFilter();

    partial void OnSelectedLanguageIndexChanged(int value)
    {
        foreach (string name in new[] { nameof(CloudPairingTitle), nameof(PhoneToPcLabel), nameof(PcToPhoneLabel), nameof(CloudPairingHelp), nameof(CloudPairingExportHelp), nameof(OptionalPairingKeyLabel), nameof(ApplyCloudPairingLabel) })
            OnPropertyChanged(name);
        foreach (string name in new[] { nameof(DownloadCredentialsLabel), nameof(CredentialCloudAuthHint), nameof(InitialSetupTitle), nameof(InitialSetupLabel), nameof(InitialSetupCloudLabel), nameof(InitialSetupLocalLabel), nameof(InitialSetupFreshLabel), nameof(InitialSetupDevicesLabel) })
            OnPropertyChanged(name);
        OnPropertyChanged(nameof(Text));
        OnPropertyChanged(nameof(SelectedAccountName));
        OnPropertyChanged(nameof(QrCaptureModeOptions));
        OnPropertyChanged(nameof(IsRussian));
        OnPropertyChanged(nameof(IncomingCommandsLabel));
        OnPropertyChanged(nameof(IncomingCommandsHelp));
        OnPropertyChanged(nameof(UpdatesTitle));
        OnPropertyChanged(nameof(StartupUpdatesLabel));
        OnPropertyChanged(nameof(CheckUpdatesLabel));
        OnPropertyChanged(nameof(OpenUpdateLabel));
        OnPropertyChanged(nameof(UpdateHelp));
        OnPropertyChanged(nameof(SteamSessionsTitle));
        OnPropertyChanged(nameof(SteamSessionsSubtitle));
        OnPropertyChanged(nameof(RefreshSteamSessionsLabel));
        OnPropertyChanged(nameof(CloseLabel));
        OnPropertyChanged(nameof(ManageSteamSessionsLabel));
        OnPropertyChanged(nameof(TerminateAllSteamSessionsLabel));
        OnPropertyChanged(nameof(EndSessionsActionTitle));
        OnPropertyChanged(nameof(ConfirmEndSessionsActionLabel));
        OnPropertyChanged(nameof(ConfirmationAutomationTitle));
        OnPropertyChanged(nameof(PeriodicConfirmationCheckingLabel));
        OnPropertyChanged(nameof(ConfirmationIntervalLabel));
        OnPropertyChanged(nameof(CheckAllConfirmationsLabel));
        OnPropertyChanged(nameof(AutoConfirmMarketLabel));
        OnPropertyChanged(nameof(AutoConfirmGiftTradesLabel));
        OnPropertyChanged(nameof(ConfirmationAutomationWarning));
        OnPropertyChanged(nameof(ConfirmationSafetyText));
        OnPropertyChanged(nameof(MonitoringNav));
        OnPropertyChanged(nameof(MonitoringTitle));
        OnPropertyChanged(nameof(MonitoringSubtitle));
        OnPropertyChanged(nameof(MonitoringAccountHeader));
        OnPropertyChanged(nameof(MonitoringSessionHeader));
        OnPropertyChanged(nameof(MonitoringGamesHeader));
        OnPropertyChanged(nameof(MonitoringInventoryHeader));
        OnPropertyChanged(nameof(MonitoringRefresh));
        OnPropertyChanged(nameof(CredentialsNav));
        OnPropertyChanged(nameof(CredentialsTitle));
        OnPropertyChanged(nameof(CredentialsSubtitle));
        OnPropertyChanged(nameof(AutoLoginAllLabel));
        OnPropertyChanged(nameof(ConfigureCredentialsLabel));
        OnPropertyChanged(nameof(AutoLoginAllPasswordExplanation));
        OnPropertyChanged(nameof(StoreLoginLabel));
        OnPropertyChanged(nameof(LoginActionLabel));
        OnPropertyChanged(nameof(RemoveLabel));
        OnPropertyChanged(nameof(EncryptionNav));
        OnPropertyChanged(nameof(EncryptionTitle));
        OnPropertyChanged(nameof(EncryptionSubtitle));
        OnPropertyChanged(nameof(EncryptionCurrentPasswordLabel));
        OnPropertyChanged(nameof(EncryptionNewPasswordLabel));
        OnPropertyChanged(nameof(EncryptionConfirmPasswordLabel));
        OnPropertyChanged(nameof(EnableEncryptionLabel));
        OnPropertyChanged(nameof(ChangeEncryptionLabel));
        OnPropertyChanged(nameof(DisableEncryptionLabel));
        OnPropertyChanged(nameof(ApplyProtectionLabel));
        OnPropertyChanged(nameof(CreateCredentialsLabel));
        OnPropertyChanged(nameof(CopyKeyLabel));
        OnPropertyChanged(nameof(VerifyKeyLabel));
        OnPropertyChanged(nameof(FinalCreateCredentialsLabel));
        OnPropertyChanged(nameof(ImportCredentialsLabel));
        OnPropertyChanged(nameof(CreateAuthenticatorLabel));
        OnPropertyChanged(nameof(UpgradeCredentialsLabel));
        OnPropertyChanged(nameof(ImportLegacyCredentialsLabel));
        OnPropertyChanged(nameof(DeleteCredentialsVaultLabel));
        OnPropertyChanged(nameof(AddCredentialLabel));
        OnPropertyChanged(nameof(CredentialVaultSectionLabel));
        OnPropertyChanged(nameof(CredentialVaultReadyTitle));
        OnPropertyChanged(nameof(CredentialVaultMissingTitle));
        OnPropertyChanged(nameof(CredentialVaultSummaryTitle));
        OnPropertyChanged(nameof(EmptyCredentialsTitle));
        OnPropertyChanged(nameof(EmptyCredentialsSubtitle));
        OnPropertyChanged(nameof(DeleteCredentialsVaultTitle));
        OnPropertyChanged(nameof(DeleteCredentialsVaultWarning));
        OnPropertyChanged(nameof(CredentialAccountLabel));
        OnPropertyChanged(nameof(CredentialUsernameLabel));
        OnPropertyChanged(nameof(CredentialPasswordLabel));
        OnPropertyChanged(nameof(SaveCredentialLabel));
        OnPropertyChanged(nameof(PlainCredentialImportTitle));
        OnPropertyChanged(nameof(PlainCredentialImportWarning));
        OnPropertyChanged(nameof(ImportKeyLabel));
        OnPropertyChanged(nameof(DevicesNav));
        OnPropertyChanged(nameof(DevicesTitle));
        OnPropertyChanged(nameof(DevicesSubtitle));
        OnPropertyChanged(nameof(DeviceRoleLabel));
        OnPropertyChanged(nameof(DeviceAccountsLabel));
        OnPropertyChanged(nameof(DeviceExpiresLabel));
        OnPropertyChanged(nameof(RelayLabel));
        OnPropertyChanged(nameof(CheckRelayLabel));
        OnPropertyChanged(nameof(ReloadRegistryLabel));
        OnPropertyChanged(nameof(RefreshDevicesLabel));
        OnPropertyChanged(nameof(DeviceNameHeader));
        OnPropertyChanged(nameof(DeviceLastSeenHeader));
        OnPropertyChanged(nameof(DeviceStateHeader));
        OnPropertyChanged(nameof(SecurityActivityLabel));
        OnPropertyChanged(nameof(RefreshActivityLabel));
        OnPropertyChanged(nameof(ConfigureAdminPinLabel));
        OnPropertyChanged(nameof(AdminPinLabel));
        OnPropertyChanged(nameof(ConfirmAdminPinLabel));
        OnPropertyChanged(nameof(LockDeviceLabel));
        OnPropertyChanged(nameof(RevokeDeviceLabel));
        OnPropertyChanged(nameof(WipeDeviceLabel));
        OnPropertyChanged(nameof(CancelLabel));
        OnPropertyChanged(nameof(ConfirmDeviceActionLabel));
        OnPropertyChanged(nameof(AddDeviceLabel));
        OnPropertyChanged(nameof(JoinDeviceLabel));
        OnPropertyChanged(nameof(JoinPairingTitle));
        OnPropertyChanged(nameof(JoinPairingHint));
        OnPropertyChanged(nameof(InvitationPayloadLabel));
        OnPropertyChanged(nameof(ScanInvitationLabel));
        OnPropertyChanged(nameof(JoinVerificationCodeLabel));
        OnPropertyChanged(nameof(JoinVaultPasswordLabel));
        OnPropertyChanged(nameof(PairingTitle));
        OnPropertyChanged(nameof(PairingRoleLabel));
        OnPropertyChanged(nameof(CreatePairingQrLabel));
        OnPropertyChanged(nameof(ShowVerificationCodeLabel));
        OnPropertyChanged(nameof(PairingVerificationLabel));
        OnPropertyChanged(nameof(PairingRoleOptions));
        OnPropertyChanged(nameof(PairingRoleHint));
        OnPropertyChanged(nameof(ConnectCloudAccountLabel));
        OnPropertyChanged(nameof(PushCloudLabel));
        foreach (AccountItemViewModel account in _allAccounts)
        {
            account.SetLanguage(value == (int)AppLanguage.Russian);
        }
        foreach (ConfirmationItemViewModel confirmation in Confirmations)
        {
            confirmation.SetLanguage(value == (int)AppLanguage.Russian);
        }
        RefreshVaultText();
        RefreshQrStateText();
        RefreshPendingConfirmationPrompt();
        RefreshHotkeyStateText();
        if (IsEncryptionVisible)
        {
            RefreshVaultProtectionStatus();
        }
        if (IsDevicesVisible)
        {
            RefreshDeviceRegistry();
        }
        SettingsNotice = string.Empty;
    }

    private void ClearLoginSecrets()
    {
        LoginPassword = string.Empty;
        LoginVaultPassword = string.Empty;
        LoginEmailCode = string.Empty;
    }

    private void ClearAccountImportSecrets()
    {
        AccountImportSourcePassword = string.Empty;
        AccountImportDestinationPassword = string.Empty;
    }

    private void ClearAdminPinFields()
    {
        AdminPin = string.Empty;
        AdminPinConfirmation = string.Empty;
    }

    private void DisposePairingQr()
    {
        if (PairingQrImage is IDisposable disposable)
            disposable.Dispose();
        PairingQrImage = null;
    }

    private IDeviceRegistryClient? EnsureEnrollmentClient()
    {
        if (_deviceRegistryClient is not null) return _deviceRegistryClient;
        if (!OperatingSystem.IsWindows()) return null;
        try
        {
            WindowsCngDeviceRequestSigner signer = new(createIfMissing: true);
            _deviceRequestSigner = signer;
            _deviceRegistryClient = new DeviceRegistryClient(new HttpClient { Timeout = TimeSpan.FromSeconds(15) }, signer, _deviceRegistryStore.Save);
            return _deviceRegistryClient;
        }
        catch (CryptographicException)
        {
            return null;
        }
    }

    private bool ImportGrantedAccounts(IReadOnlyList<GrantedAccountPayload> accounts, string destinationPassword, out string? detail)
    {
        detail = null;
        if (accounts.Count == 0) return true;

        Directory.CreateDirectory(_maFilesDirectory);
        string manifestPath = Path.Combine(_maFilesDirectory, "manifest.json");
        byte[]? originalManifest = File.Exists(manifestPath) ? File.ReadAllBytes(manifestPath) : null;
        HashSet<string> originalFiles = Directory.GetFiles(_maFilesDirectory, "*.maFile")
            .Select(Path.GetFullPath).ToHashSet(StringComparer.OrdinalIgnoreCase);
        string staging = Path.Combine(Path.GetTempPath(), "SDAPlusPlus-claim-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(staging);
        try
        {
            foreach (GrantedAccountPayload account in accounts)
            {
                JObject payload = JObject.Parse(Encoding.UTF8.GetString(account.MaFilePayload));
                string payloadSteamId = payload["Session"]?.Value<string>("SteamID") ?? string.Empty;
                if (!string.Equals(payloadSteamId, account.SteamId, StringComparison.Ordinal))
                    throw new CryptographicException("An offline account payload does not match its granted SteamID.");

                string source = Path.Combine(staging, account.SteamId + ".maFile");
                File.WriteAllBytes(source, account.MaFilePayload);
                VaultAccountImportResult result = _accountImportService.Import(source, _maFilesDirectory, destinationPassword: destinationPassword);
                File.Delete(source);
                if (result.State is not (VaultAccountImportState.Imported or VaultAccountImportState.Duplicate))
                    throw new InvalidOperationException(result.Detail ?? $"Account import failed: {result.State}.");
            }
            return true;
        }
        catch (Exception ex) when (ex is CryptographicException or JsonException or IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            foreach (string file in Directory.GetFiles(_maFilesDirectory, "*.maFile").Select(Path.GetFullPath))
                if (!originalFiles.Contains(file)) File.Delete(file);
            if (originalManifest is null)
            {
                if (File.Exists(manifestPath)) File.Delete(manifestPath);
            }
            else
            {
                File.WriteAllBytes(manifestPath, originalManifest);
                CryptographicOperations.ZeroMemory(originalManifest);
            }
            detail = ex.Message;
            return false;
        }
        finally
        {
            if (originalManifest is not null) CryptographicOperations.ZeroMemory(originalManifest);
            try { Directory.Delete(staging, recursive: true); } catch (IOException) { }
        }
    }

    private void ClearJoinPairingSecrets()
    {
        JoinPairingPayload = string.Empty;
        JoinVerificationCode = string.Empty;
        JoinVaultPassword = string.Empty;
    }

    private async Task<string?> RequestEmailCodeAsync(string email, bool previousCodeWasIncorrect, CancellationToken cancellationToken)
    {
        var completion = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            _emailCodeCompletion = completion;
            LoginEmailCode = string.Empty;
            LoginEmailPrompt = string.Format(Text.EmailCodePromptFormat, string.IsNullOrWhiteSpace(email) ? "email" : email);
            LoginStatus = previousCodeWasIncorrect ? Text.LoginEmailCodeRequired : string.Empty;
            IsLoginEmailCodeRequested = true;
        });

        using CancellationTokenRegistration registration = cancellationToken.Register(() => completion.TrySetCanceled(cancellationToken));
        string? code = await completion.Task;
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            IsLoginEmailCodeRequested = false;
            _emailCodeCompletion = null;
        });
        return code;
    }

    partial void OnAccountSearchEnabledChanged(bool value)
    {
        if (!value)
        {
            SearchText = string.Empty;
        }
    }

    partial void OnFavoritesFirstChanged(bool value) => ApplyAccountFilter();

    partial void OnPeriodicConfirmationCheckingChanged(bool value)
    {
        if (_confirmationPollingTimer is not null) UpdateConfirmationPollingTimer();
    }

    partial void OnConfirmationCheckIntervalSecondsChanged(int value)
    {
        if (_confirmationPollingTimer is not null) UpdateConfirmationPollingTimer();
    }

    partial void OnAutoConfirmMarketTransactionsChanged(bool value) =>
        OnPropertyChanged(nameof(ConfirmationSafetyText));

    partial void OnAutoConfirmGiftTradesChanged(bool value) =>
        OnPropertyChanged(nameof(ConfirmationSafetyText));

    partial void OnSelectedQrCaptureModeIndexChanged(int value) => OnPropertyChanged(nameof(IsCursorAreaMode));

    partial void OnSelectedCloudProviderIndexChanged(int value)
    {
        OnPropertyChanged(nameof(IsWebDavProvider));
        OnPropertyChanged(nameof(IsS3Provider));
        OnPropertyChanged(nameof(IsDropboxProvider));
        OnPropertyChanged(nameof(IsOneDriveProvider));
        OnPropertyChanged(nameof(IsGoogleDriveProvider));
        OnPropertyChanged(nameof(IsOAuthCloudProvider));
        CloudStatus = string.Empty;
        CloudPreviewSummary = string.Empty;
        ClearCloudSecrets();
    }

    partial void OnIsSettingsVisibleChanged(bool value) => OnPropertyChanged(nameof(IsDashboardVisible));

    partial void OnIsCloudVisibleChanged(bool value) => OnPropertyChanged(nameof(IsDashboardVisible));

    partial void OnIsQrVisibleChanged(bool value) => OnPropertyChanged(nameof(IsDashboardVisible));

    partial void OnIsConfirmationsVisibleChanged(bool value) => OnPropertyChanged(nameof(IsDashboardVisible));

    partial void OnIsMonitoringVisibleChanged(bool value) => OnPropertyChanged(nameof(IsDashboardVisible));

    partial void OnIsCredentialsVisibleChanged(bool value) => OnPropertyChanged(nameof(IsDashboardVisible));

    partial void OnIsEncryptionVisibleChanged(bool value) => OnPropertyChanged(nameof(IsDashboardVisible));

    partial void OnIsDevicesVisibleChanged(bool value) => OnPropertyChanged(nameof(IsDashboardVisible));

    partial void OnSelectedPairingRoleIndexChanged(int value)
    {
        OnPropertyChanged(nameof(PairingRoleHint));
        OnPropertyChanged(nameof(IsPairingAccountSelectionVisible));
    }

    partial void OnPairingQrImageChanged(IImage? value)
    {
        OnPropertyChanged(nameof(IsPairingAccountSelectionVisible));
        OnPropertyChanged(nameof(IsPairingQrVisible));
    }

    private void ApplyAccountFilter()
    {
        AccountItemViewModel? selected = SelectedAccount;
        string query = SearchText.Trim();
        IEnumerable<AccountItemViewModel> matches = !AccountSearchEnabled || string.IsNullOrEmpty(query)
            ? _allAccounts
            : _allAccounts.Where(account =>
                account.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                account.SteamId.Contains(query, StringComparison.OrdinalIgnoreCase));

        if (FavoritesFirst)
        {
            matches = matches.OrderByDescending(account => account.IsFavorite).ThenBy(account => account.Name, StringComparer.OrdinalIgnoreCase);
        }

        Accounts.Clear();
        foreach (AccountItemViewModel account in matches)
        {
            Accounts.Add(account);
        }

        SelectedAccount = selected is not null && Accounts.Contains(selected)
            ? selected
            : Accounts.FirstOrDefault();
    }

    private void RefreshStoredCredentials(bool preserveStatus = false)
    {
        CredentialVaultLoadResult result = _credentialVault.Load();
        IsCredentialVaultReady = result.IsReady;
        IsCredentialVaultMissing = result.State == CredentialVaultState.Missing;
        OnPropertyChanged(nameof(CredentialVaultSummaryTitle));
        StoredCredentials.Clear();
        CredentialAccounts.Clear();
        if (result.IsReady)
        {
            foreach (StoredCredential credential in result.Credentials.OrderBy(item => item.AccountName, StringComparer.OrdinalIgnoreCase))
            {
                StoredCredentials.Add(new CredentialItemViewModel(credential));
            }

        }

        foreach (AccountItemViewModel account in _allAccounts.OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase))
        {
            StoredCredential? existing = result.Credentials.FirstOrDefault(item =>
                (ulong.TryParse(account.SteamId, out ulong steamId) && steamId > 0 && item.SteamId == steamId) ||
                string.Equals(item.AccountName, account.Name, StringComparison.OrdinalIgnoreCase));
            CredentialAccounts.Add(new AccountCredentialRowViewModel(account, existing, IsRussian));
        }

        if (!preserveStatus)
        {
            CredentialsStatus = result.State switch
            {
                CredentialVaultState.Ready => IsRussian
                    ? $"Зашифровано: {(result.Encrypted ? "да" : "нет")}. Записей: {result.Credentials.Count}."
                    : $"Encrypted: {(result.Encrypted ? "yes" : "no")}. Entries: {result.Credentials.Count}.",
                CredentialVaultState.Missing => IsRussian ? "credentials.secure.json пока не создан." : "credentials.secure.json has not been created yet.",
                CredentialVaultState.Locked => IsRussian ? "Файл зашифрован, но DPAPI-ключ недоступен." : "The file is encrypted, but its DPAPI key is unavailable.",
                _ => result.Detail ?? (IsRussian ? "Файл логинов повреждён." : "The credentials file is invalid.")
            };
        }
        OnPropertyChanged(nameof(IsCredentialEmptyStateVisible));
    }

    private CloudPushPackage AttachEncryptedCredentials(CloudPushPackage package)
    {
        string credentialsPath = Path.Combine(_maFilesDirectory, "credentials.secure.json");
        if (!File.Exists(credentialsPath)) return package;

        try
        {
            string json = File.ReadAllText(credentialsPath);
            JObject document = JObject.Parse(json);
            if (document.Value<bool?>("encrypted") != true || string.IsNullOrWhiteSpace(document.Value<string>("payload")))
            {
                return package;
            }

            Dictionary<string, string> files = new(package.AccountFiles, StringComparer.OrdinalIgnoreCase)
            {
                ["credentials.secure.json"] = json
            };
            return package with { AccountFiles = files };
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            CloudStatus = IsRussian
                ? "Не удалось добавить credentials.secure.json: " + exception.Message
                : "Could not attach credentials.secure.json: " + exception.Message;
            return package;
        }
    }

    private void PrepareCredentialEditor(AccountItemViewModel? account)
    {
        CredentialEditorPassword = string.Empty;
        if (account is null)
        {
            CredentialEditorUsername = string.Empty;
            return;
        }

        CredentialEditorUsername = _credentialVault.TryGet(account.Account, out StoredCredential? credential) && credential is not null
            ? credential.Username
            : account.Name;
    }

    private void ApplyVaultResult(VaultLoadResult result)
    {
        _allAccounts.Clear();
        _deviceRegistry = _deviceRegistryStore.Load();
        DeviceRegistrySettings registry = _deviceRegistry.Settings;
        IEnumerable<VaultAccount> visibleAccounts = result.Accounts;
        if (_deviceRegistry.State == DeviceRegistryLoadState.Ready && registry.IsConfigured && registry.Role != "Admin")
        {
            visibleAccounts = registry.IsExpired
                ? []
                : result.Accounts.Where(account => registry.GrantedAccountSteamIds.Contains(account.SteamId));
        }

        foreach (VaultAccount account in visibleAccounts)
        {
            var viewModel = new AccountItemViewModel(account, SelectedLanguageIndex == (int)AppLanguage.Russian);
            _allAccounts.Add(viewModel);
            _ = viewModel.LoadAvatarAsync();
        }

        IsVaultLocked = result.RequiresPassword;
        IsVaultError = result.State is VaultLoadState.Corrupt or VaultLoadState.Missing;
        VaultStateTitle = result.State switch
        {
            VaultLoadState.Locked => Text.VaultLockedTitle,
            VaultLoadState.InvalidPassword => Text.WrongPasswordTitle,
            VaultLoadState.Missing => Text.VaultMissingTitle,
            VaultLoadState.Corrupt => Text.VaultAttentionTitle,
            _ => string.Empty
        };
        VaultStateDetail = result.State switch
        {
            VaultLoadState.Locked => Text.VaultLockedDetail,
            VaultLoadState.InvalidPassword => Text.WrongPasswordDetail,
            VaultLoadState.Missing => Text.VaultMissingDetail,
            VaultLoadState.Corrupt => result.Detail ?? "The manifest or account files could not be read.",
            _ => string.Empty
        };

        StatusMessage = result.State switch
        {
            VaultLoadState.Ready => string.Format(Text.AccountsReadyFormat, result.Accounts.Count),
            VaultLoadState.Partial => string.Format(Text.AccountsPartialFormat, result.Accounts.Count, result.SkippedFiles),
            VaultLoadState.Locked => Text.EncryptedVault,
            VaultLoadState.InvalidPassword => Text.VaultRemainsLocked,
            VaultLoadState.Missing => Text.NoLocalVault,
            _ => Text.VaultUnavailable
        };

        ApplyAccountFilter();
    }

    private void RefreshVaultText()
    {
        if (IsVaultLocked)
        {
            bool wrongPassword = VaultStateTitle == UiStrings.For(AppLanguage.English).WrongPasswordTitle ||
                                 VaultStateTitle == UiStrings.For(AppLanguage.Russian).WrongPasswordTitle;
            VaultStateTitle = wrongPassword ? Text.WrongPasswordTitle : Text.VaultLockedTitle;
            VaultStateDetail = wrongPassword ? Text.WrongPasswordDetail : Text.VaultLockedDetail;
            StatusMessage = wrongPassword ? Text.VaultRemainsLocked : Text.EncryptedVault;
        }
        else if (IsVaultError)
        {
            VaultStateTitle = Text.VaultAttentionTitle;
        }
        else
        {
            StatusMessage = string.Format(Text.AccountsReadyFormat, _allAccounts.Count);
        }
    }

    private void RefreshCodes()
    {
        long unixTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        SelectedAccount?.RefreshCode(unixTime);
    }

    private void SetPage(
        bool settings,
        bool cloud,
        bool qr,
        bool confirmations,
        bool monitoring = false,
        bool credentials = false,
        bool encryption = false,
        bool devices = false)
    {
        _navigation.Visit(settings ? "settings" : cloud ? "cloud" : qr ? "qr" : confirmations ? "confirmations" :
            monitoring ? "monitoring" : credentials ? "credentials" : encryption ? "encryption" : devices ? "devices" : "dashboard");
        IsSettingsVisible = settings;
        IsCloudVisible = cloud;
        IsQrVisible = qr;
        IsConfirmationsVisible = confirmations;
        IsMonitoringVisible = monitoring;
        IsCredentialsVisible = credentials;
        IsEncryptionVisible = encryption;
        IsDevicesVisible = devices;
        OnPropertyChanged(nameof(CanNavigateBack));
        OnPropertyChanged(nameof(CanNavigateForward));
    }

    private void PrepareProtectionChange(bool enable, bool requireCurrentPassword)
    {
        if (IsVaultProtectionBusy) return;

        VaultEncryptionStatus status = _vaultEncryptionService.Inspect(_maFilesDirectory);
        IsVaultProtectionEncrypted = status.IsEncrypted;
        if (!status.Exists)
        {
            VaultProtectionStatus = status.Detail ?? (IsRussian ? "Хранилище не найдено." : "The vault was not found.");
            return;
        }

        if (enable && status.IsEncrypted && !requireCurrentPassword)
        {
            VaultProtectionStatus = IsRussian ? "Шифрование уже включено. Используйте смену пароля." : "Encryption is already enabled. Use Change password.";
            return;
        }

        if (requireCurrentPassword && !status.IsEncrypted)
        {
            VaultProtectionStatus = IsRussian ? "Хранилище сейчас не зашифровано." : "The vault is not currently encrypted.";
            return;
        }

        if (requireCurrentPassword && string.IsNullOrEmpty(VaultProtectionCurrentPassword))
        {
            VaultProtectionStatus = IsRussian ? "Введите текущий пароль." : "Enter the current password.";
            return;
        }

        if (enable)
        {
            if (string.IsNullOrEmpty(VaultProtectionNewPassword))
            {
                VaultProtectionStatus = IsRussian ? "Введите новый пароль." : "Enter a new password.";
                return;
            }

            if (!string.Equals(VaultProtectionNewPassword, VaultProtectionConfirmPassword, StringComparison.Ordinal))
            {
                VaultProtectionStatus = IsRussian ? "Новые пароли не совпадают." : "The new passwords do not match.";
                return;
            }
        }

        _pendingProtectionEnabled = enable;
        VaultProtectionPrompt = enable
            ? (status.IsEncrypted
                ? (IsRussian ? $"Сменить пароль шифрования для {status.AccountCount} аккаунтов?" : $"Change the encryption password for {status.AccountCount} accounts?")
                : (IsRussian ? $"Зашифровать {status.AccountCount} аккаунтов?" : $"Encrypt {status.AccountCount} accounts?"))
            : (IsRussian
                ? $"Снять шифрование с {status.AccountCount} аккаунтов? Локальные .maFile станут читаемыми без пароля."
                : $"Remove encryption from {status.AccountCount} accounts? Local .maFile data will become readable without a password.");
        IsProtectionConfirmationVisible = true;
        VaultProtectionStatus = IsRussian
            ? "Изменения ещё не применены. Подтвердите операцию."
            : "No changes have been applied yet. Confirm the operation.";
    }

    private void RefreshVaultProtectionStatus()
    {
        VaultEncryptionStatus status = _vaultEncryptionService.Inspect(_maFilesDirectory);
        IsVaultProtectionEncrypted = status.IsEncrypted;
        VaultProtectionStatus = !status.Exists
            ? status.Detail ?? (IsRussian ? "Хранилище не найдено." : "The vault was not found.")
            : status.IsEncrypted
                ? (IsRussian ? $"Защита включена. Зашифровано аккаунтов: {status.AccountCount}." : $"Protection enabled. Encrypted accounts: {status.AccountCount}.")
                : (IsRussian ? $"Защита отключена. Аккаунтов в хранилище: {status.AccountCount}." : $"Protection disabled. Accounts in vault: {status.AccountCount}.");
    }

    private void ClearVaultProtectionSecrets()
    {
        VaultProtectionCurrentPassword = string.Empty;
        VaultProtectionNewPassword = string.Empty;
        VaultProtectionConfirmPassword = string.Empty;
    }

    private void ClearCredentialSetup()
    {
        _credentialSetupCountdown?.Cancel();
        _credentialSetupCountdown?.Dispose();
        _credentialSetupCountdown = null;
        GeneratedCredentialKey = string.Empty;
        CredentialKeyConfirmation = string.Empty;
        CredentialSetupStatus = string.Empty;
        CanContinueCredentialSetup = false;
        IsCredentialKeyReentryVisible = false;
        IsCredentialFinalWarningVisible = false;
        IsCredentialSetupVisible = false;
    }

    private static bool CredentialKeysMatch(string expected, string entered)
    {
        byte[] expectedBytes = System.Text.Encoding.UTF8.GetBytes(expected);
        byte[] enteredBytes = System.Text.Encoding.UTF8.GetBytes(entered);
        try
        {
            return CryptographicOperations.FixedTimeEquals(expectedBytes, enteredBytes);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(expectedBytes);
            CryptographicOperations.ZeroMemory(enteredBytes);
        }
    }

    private void QueueConfirmationAction(ConfirmationItemViewModel? item, ConfirmationDecision decision)
    {
        if (item is null || IsConfirmationBusy)
        {
            return;
        }

        _pendingConfirmation = item;
        _pendingConfirmationDecision = decision;
        IsConfirmationActionPending = true;
        RefreshPendingConfirmationPrompt();
    }

    private void RefreshPendingConfirmationPrompt()
    {
        if (_pendingConfirmation is null)
        {
            return;
        }

        string format = _pendingConfirmationDecision == ConfirmationDecision.Accept
            ? Text.AcceptConfirmationPromptFormat
            : Text.RejectConfirmationPromptFormat;
        ConfirmationActionPrompt = string.Format(format, _pendingConfirmation.Headline, _pendingConfirmation.AccountName);
    }

    private void ConfigureHotkeys()
    {
        if (!HotkeyGestureParser.TryParse(QrToggleHotkey, out HotkeyGesture? toggleGesture) ||
            !HotkeyGestureParser.TryParse(QrScanHotkey, out HotkeyGesture? scanGesture) ||
            !HotkeyGestureParser.TryParse(PreviousAccountHotkey, out HotkeyGesture? previousGesture) ||
            !HotkeyGestureParser.TryParse(NextAccountHotkey, out HotkeyGesture? nextGesture) ||
            !HotkeyGestureParser.TryParse(AutoLoginAllHotkey, out HotkeyGesture? autoLoginGesture) ||
            !HotkeyGestureParser.TryParse(ConfirmationsHotkey, out HotkeyGesture? confirmationsGesture))
        {
            SetHotkeyState(HotkeyUiState.Invalid);
            return;
        }

        HotkeyRegistrationResult result = _globalHotkeyService.Configure(new GlobalHotkeyConfiguration(
            GlobalHotkeysEnabled,
            toggleGesture!,
            scanGesture!,
            previousGesture!,
            nextGesture!,
            autoLoginGesture!,
            confirmationsGesture!));
        SetHotkeyState(result.Success
            ? (GlobalHotkeysEnabled ? HotkeyUiState.Active : HotkeyUiState.Disabled)
            : result.Failure switch
            {
                HotkeyRegistrationFailure.Duplicate => HotkeyUiState.Duplicate,
                HotkeyRegistrationFailure.AlreadyInUse => HotkeyUiState.AlreadyInUse,
                _ => HotkeyUiState.Unavailable
            });
    }

    private void OnGlobalScanQrRequested(object? sender, EventArgs e) =>
        Dispatcher.UIThread.Post(async () =>
        {
            if (!QrHotkeysEnabled)
            {
                _hotkeyOverlayService.Show("Steam QR", Text.QrHotkeysDisabledStatus);
                return;
            }

            _hotkeyOverlayService.Show("Steam QR", Text.QrScanning);
            ShowQr();
            await ScanQrAsync();
            _hotkeyOverlayService.Show("Steam QR", QrStatus);
        });

    private void OnGlobalConfirmationsRequested(object? sender, EventArgs e) =>
        Dispatcher.UIThread.Post(async () => await ShowConfirmationsAsync());

    private void OnGlobalToggleQrRequested(object? sender, EventArgs e) =>
        Dispatcher.UIThread.Post(() =>
        {
            QrHotkeysEnabled = !QrHotkeysEnabled;
            _preferenceStore.Save(CreatePreferences());
            _hotkeyOverlayService.Show("Steam QR", QrHotkeysEnabled ? Text.QrHotkeysEnabledStatus : Text.QrHotkeysDisabledStatus);
        });

    private void OnGlobalPreviousAccountRequested(object? sender, EventArgs e) =>
        Dispatcher.UIThread.Post(() => SelectRelativeAccount(-1));

    private void OnGlobalNextAccountRequested(object? sender, EventArgs e) =>
        Dispatcher.UIThread.Post(() => SelectRelativeAccount(1));

    private void OnGlobalAutoLoginAllRequested(object? sender, EventArgs e) =>
        Dispatcher.UIThread.Post(() => _ = ShowAutoLoginAllPanelAsync());

    private void SelectRelativeAccount(int offset)
    {
        if (Accounts.Count == 0)
        {
            _hotkeyOverlayService.Show(Text.ActiveAccount, Text.NoAccountSelected);
            return;
        }

        int index = SelectedAccount is null ? 0 : Accounts.IndexOf(SelectedAccount);
        index = index < 0 ? 0 : (index + offset + Accounts.Count) % Accounts.Count;
        SelectedAccount = Accounts[index];
        _hotkeyOverlayService.Show(Text.ActiveAccount, $"{SelectedAccount.Name}  ·  {SelectedAccount.Code}");
    }

    private HotkeyUiState _hotkeyState;

    private void SetHotkeyState(HotkeyUiState state)
    {
        _hotkeyState = state;
        RefreshHotkeyStateText();
    }

    private void RefreshHotkeyStateText() => HotkeyStatus = _hotkeyState switch
    {
        HotkeyUiState.Active => Text.HotkeysActive,
        HotkeyUiState.Disabled => Text.HotkeysDisabled,
        HotkeyUiState.Invalid => Text.HotkeysInvalid,
        HotkeyUiState.Duplicate => Text.HotkeysDuplicate,
        HotkeyUiState.AlreadyInUse => Text.HotkeysAlreadyInUse,
        _ => Text.HotkeysUnavailable
    };

    private QrUiState _qrState;

    private void SetQrState(QrUiState state)
    {
        _qrState = state;
        RefreshQrStateText();
    }

    private void RefreshQrStateText() => QrStatus = _qrState switch
    {
        QrUiState.Scanning => Text.QrScanning,
        QrUiState.Approving => Text.QrApproving,
        QrUiState.Approved => Text.QrApproved,
        QrUiState.NotFound => Text.QrNotFound,
        QrUiState.Unsupported => Text.QrUnsupported,
        QrUiState.NoAccount => Text.QrNoAccount,
        QrUiState.MissingSession => Text.QrNoSession,
        QrUiState.ExpiredSession => Text.QrExpiredSession,
        QrUiState.MissingSecret => Text.QrMissingSecret,
        QrUiState.UnsupportedPlatform => Text.QrUnsupportedPlatform,
        QrUiState.Rejected => Text.QrRejected,
        _ => Text.QrReady
    };

    private CloudConnectionSettings CreateCloudSettings() => (CloudProviderKind)Math.Clamp(SelectedCloudProviderIndex, 0, 4) switch
    {
        CloudProviderKind.S3Compatible => new(S3Endpoint, S3AccessKey, S3SecretKey, S3RemotePath, CloudProviderKind.S3Compatible, S3Bucket),
        CloudProviderKind.Dropbox => new(string.Empty, string.Empty, DropboxAccessToken, DropboxRemotePath, CloudProviderKind.Dropbox),
        CloudProviderKind.OneDrivePersonal => new(string.Empty, string.Empty, string.Empty, OneDriveRemotePath, CloudProviderKind.OneDrivePersonal, ClientId: OneDriveClientId),
        CloudProviderKind.GoogleDrive => new(string.Empty, string.Empty, string.Empty, GoogleDriveRemotePath, CloudProviderKind.GoogleDrive, ClientId: GoogleDriveClientId),
        _ => new(WebDavEndpoint, WebDavUsername, WebDavPassword, WebDavRemotePath)
    };

    private AppPreferences CreatePreferences() => new()
    {
        InitialSetupCompleted = _initialSetupCompleted,
        CheckUpdatesOnStartup = CheckUpdatesOnStartup,
        Language = (AppLanguage)Math.Clamp(SelectedLanguageIndex, 0, 1),
        Theme = (ThemePreset)Math.Clamp(SelectedThemeIndex, 0, 2),
        AccountSearchEnabled = AccountSearchEnabled,
        FavoritesFirst = FavoritesFirst,
        PeriodicConfirmationChecking = PeriodicConfirmationChecking,
        ConfirmationCheckIntervalSeconds = Math.Clamp(ConfirmationCheckIntervalSeconds, 5, 3600),
        CheckAllAccountsForConfirmations = CheckAllAccountsForConfirmations,
        AutoConfirmMarketTransactions = AutoConfirmMarketTransactions,
        AutoConfirmGiftTrades = AutoConfirmGiftTrades,
        QrCaptureMode = (QrCaptureMode)Math.Clamp(SelectedQrCaptureModeIndex, 0, 2),
        QrCursorAreaSize = Math.Clamp(QrCursorAreaSize, 250, 2000),
        GlobalHotkeysEnabled = GlobalHotkeysEnabled,
        QrHotkeysEnabled = QrHotkeysEnabled,
        QrToggleHotkey = QrToggleHotkey,
        QrScanHotkey = QrScanHotkey,
        PreviousAccountHotkey = PreviousAccountHotkey,
        NextAccountHotkey = NextAccountHotkey,
        AutoLoginAllHotkey = AutoLoginAllHotkey,
        ConfirmationsHotkey = ConfirmationsHotkey,
        WebDavEndpoint = WebDavEndpoint.Trim(),
        WebDavUsername = WebDavUsername.Trim(),
        WebDavRemotePath = string.IsNullOrWhiteSpace(WebDavRemotePath) ? "SDAppVault" : WebDavRemotePath.Trim()
        ,CloudProvider = (CloudProviderKind)Math.Clamp(SelectedCloudProviderIndex, 0, 4)
        ,S3Endpoint = S3Endpoint.Trim()
        ,S3Bucket = S3Bucket.Trim()
        ,S3AccessKey = S3AccessKey.Trim()
        ,S3RemotePath = string.IsNullOrWhiteSpace(S3RemotePath) ? "SDAppVault" : S3RemotePath.Trim()
        ,DropboxRemotePath = string.IsNullOrWhiteSpace(DropboxRemotePath) ? "SDAppVault" : DropboxRemotePath.Trim()
        ,OneDriveClientId = OneDriveClientId.Trim()
        ,OneDriveRemotePath = string.IsNullOrWhiteSpace(OneDriveRemotePath) ? "SDAppVault" : OneDriveRemotePath.Trim()
        ,GoogleDriveClientId = GoogleDriveClientId.Trim()
        ,GoogleDriveRemotePath = string.IsNullOrWhiteSpace(GoogleDriveRemotePath) ? "SDAppVault" : GoogleDriveRemotePath.Trim()
    };

    private void ClearCloudSecrets()
    {
        WebDavPassword = string.Empty;
        S3SecretKey = string.Empty;
        DropboxAccessToken = string.Empty;
    }

    private static string ResolveMaFilesDirectory()
    {
        string? configured = Environment.GetEnvironmentVariable("SDAPP_MAFILES");
        return !string.IsNullOrWhiteSpace(configured)
            ? Path.GetFullPath(configured)
            : Path.Combine(AppContext.BaseDirectory, "maFiles");
    }

    public void Dispose()
    {
        CloseCloudPairing();
        _pairedCredentialsKey = string.Empty;
        CleanupDownloadedCredentials();
        _pendingCredentialConnection = null;
        CredentialCloudPassword = string.Empty;
        _confirmationRefreshCancellation?.Cancel();
        _confirmationRefreshCancellation?.Dispose();
        _credentialSetupCountdown?.Cancel();
        _credentialSetupCountdown?.Dispose();
        _codeTimer.Stop();
        _confirmationPollingTimer.Stop();
        _globalHotkeyService.ScanQrRequested -= OnGlobalScanQrRequested;
        _globalHotkeyService.ConfirmationsRequested -= OnGlobalConfirmationsRequested;
        _globalHotkeyService.ToggleQrRequested -= OnGlobalToggleQrRequested;
        _globalHotkeyService.PreviousAccountRequested -= OnGlobalPreviousAccountRequested;
        _globalHotkeyService.NextAccountRequested -= OnGlobalNextAccountRequested;
        _globalHotkeyService.AutoLoginAllRequested -= OnGlobalAutoLoginAllRequested;
        _globalHotkeyService.Dispose();
        _hotkeyOverlayService.Dispose();
        _deviceRequestSigner?.Dispose();
        DisposePairingQr();
    }

    private enum HotkeyUiState
    {
        Active,
        Disabled,
        Invalid,
        Duplicate,
        AlreadyInUse,
        Unavailable
    }

    private enum QrUiState
    {
        Ready,
        Scanning,
        Approving,
        Approved,
        NotFound,
        Unsupported,
        NoAccount,
        MissingSession,
        ExpiredSession,
        MissingSecret,
        UnsupportedPlatform,
        Rejected
    }
}
