using SDAPlusPlus.Core.Preferences;

namespace SDAPlusPlus.Core.Localization;

public sealed record UiStrings
{
    public required string Dashboard { get; init; }
    public required string Confirmations { get; init; }
    public required string QrSignIn { get; init; }
    public required string CloudVault { get; init; }
    public required string Settings { get; init; }
    public required string VaultStatus { get; init; }
    public required string LocalProtection { get; init; }
    public required string VaultReady { get; init; }
    public required string DashboardTitle { get; init; }
    public required string DashboardSubtitle { get; init; }
    public required string ScanQr { get; init; }
    public required string AddAccount { get; init; }
    public required string Accounts { get; init; }
    public required string Reload { get; init; }
    public required string SearchAccounts { get; init; }
    public required string SteamGuardCode { get; init; }
    public required string CopyCode { get; init; }
    public required string SelectedAccount { get; init; }
    public required string NoAccountSelected { get; init; }
    public required string Favorite { get; init; }
    public required string SessionLive { get; init; }
    public required string VaultLinked { get; init; }
    public required string LoginAgain { get; init; }
    public required string EndSessions { get; init; }
    public required string Security { get; init; }
    public required string ProtectedLocally { get; init; }
    public required string SecurityDescription { get; init; }
    public required string CloudSync { get; init; }
    public required string ReadyToConnect { get; init; }
    public required string CloudDescription { get; init; }
    public required string SettingsSubtitle { get; init; }
    public required string BackToDashboard { get; init; }
    public required string Appearance { get; init; }
    public required string Theme { get; init; }
    public required string Language { get; init; }
    public required string InterfaceLanguage { get; init; }
    public required string AccountPreferences { get; init; }
    public required string EnableSearch { get; init; }
    public required string FavoritesFirst { get; init; }
    public required string Save { get; init; }
    public required string PreferencesSaved { get; init; }
    public required string CopySteamId { get; init; }
    public required string UnlockVault { get; init; }
    public required string VaultPassword { get; init; }
    public required string TryAgain { get; init; }
    public required string VaultLockedTitle { get; init; }
    public required string WrongPasswordTitle { get; init; }
    public required string VaultMissingTitle { get; init; }
    public required string VaultAttentionTitle { get; init; }
    public required string VaultLockedDetail { get; init; }
    public required string WrongPasswordDetail { get; init; }
    public required string VaultMissingDetail { get; init; }
    public required string AccountsReadyFormat { get; init; }
    public required string AccountsPartialFormat { get; init; }
    public required string EncryptedVault { get; init; }
    public required string VaultRemainsLocked { get; init; }
    public required string NoLocalVault { get; init; }
    public required string VaultUnavailable { get; init; }
    public required string CloudTitle { get; init; }
    public required string CloudSubtitle { get; init; }
    public required string Provider { get; init; }
    public required string WebDavEndpoint { get; init; }
    public required string Username { get; init; }
    public required string Password { get; init; }
    public required string RemoteFolder { get; init; }
    public required string TestConnection { get; init; }
    public required string PullPreview { get; init; }
    public required string SaveProfile { get; init; }
    public required string PasswordSessionOnly { get; init; }
    public required string CloudConnected { get; init; }
    public required string CloudPreviewFoundFormat { get; init; }
    public required string Yes { get; init; }
    public required string No { get; init; }
    public required string FallbackListing { get; init; }
    public required string PreviewSafety { get; init; }
    public required string PrepareImport { get; init; }
    public required string ConfirmImport { get; init; }
    public required string Cancel { get; init; }
    public required string ImportWarningFormat { get; init; }
    public required string ImportCompleteFormat { get; init; }
    public required string QrTitle { get; init; }
    public required string QrSubtitle { get; init; }
    public required string QrSelectedAccount { get; init; }
    public required string ScanScreen { get; init; }
    public required string QrReady { get; init; }
    public required string QrScanning { get; init; }
    public required string QrApproving { get; init; }
    public required string QrApproved { get; init; }
    public required string QrNotFound { get; init; }
    public required string QrUnsupported { get; init; }
    public required string QrNoAccount { get; init; }
    public required string QrNoSession { get; init; }
    public required string QrExpiredSession { get; init; }
    public required string QrMissingSecret { get; init; }
    public required string QrUnsupportedPlatform { get; init; }
    public required string QrRejected { get; init; }
    public required string QrPrivacy { get; init; }
    public required string QrHowTo { get; init; }
    public required string ConfirmationsTitle { get; init; }
    public required string ConfirmationsSubtitle { get; init; }
    public required string RefreshConfirmations { get; init; }
    public required string LoadingConfirmations { get; init; }
    public required string NoConfirmationsFormat { get; init; }
    public required string ConfirmationsLoadedFormat { get; init; }
    public required string ConfirmationsLoadFailed { get; init; }
    public required string Accept { get; init; }
    public required string Reject { get; init; }
    public required string ConfirmDecision { get; init; }
    public required string AcceptConfirmationPromptFormat { get; init; }
    public required string RejectConfirmationPromptFormat { get; init; }
    public required string ConfirmationActionSucceeded { get; init; }
    public required string ConfirmationActionFailed { get; init; }
    public required string ConfirmationSafety { get; init; }
    public required string QrCaptureSettings { get; init; }
    public required string QrCaptureSource { get; init; }
    public required string QrFullDesktop { get; init; }
    public required string QrMonitorUnderCursor { get; init; }
    public required string QrAreaAroundCursor { get; init; }
    public required string QrCursorAreaSize { get; init; }
    public required string GlobalHotkeys { get; init; }
    public required string EnableGlobalHotkeys { get; init; }
    public required string ScanQrHotkey { get; init; }
    public required string ConfirmationsHotkey { get; init; }
    public required string HotkeysActive { get; init; }
    public required string HotkeysDisabled { get; init; }
    public required string HotkeysInvalid { get; init; }
    public required string HotkeysDuplicate { get; init; }
    public required string HotkeysAlreadyInUse { get; init; }
    public required string HotkeysUnavailable { get; init; }
    public required string SteamLoginTitle { get; init; }
    public required string SteamLoginSubtitle { get; init; }
    public required string SteamPassword { get; init; }
    public required string LoginAndSave { get; init; }
    public required string LoginInProgress { get; init; }
    public required string LoginSucceeded { get; init; }
    public required string LoginFailed { get; init; }
    public required string LoginMissingAccount { get; init; }
    public required string LoginVaultPasswordHint { get; init; }
    public required string LoginEmailCodeRequired { get; init; }
    public required string EmailCode { get; init; }
    public required string SubmitEmailCode { get; init; }
    public required string EmailCodePromptFormat { get; init; }
    public required string EndSessionsTitle { get; init; }
    public required string EndSessionsWarningFormat { get; init; }
    public required string ConfirmEndSessions { get; init; }
    public required string EndingSessions { get; init; }
    public required string SessionsEnded { get; init; }
    public required string SessionsEndFailed { get; init; }
    public required string SessionsMissing { get; init; }
    public required string SessionsEndedSaveFailed { get; init; }
    public required string RemoveFavorite { get; init; }
    public required string FavoriteAdded { get; init; }
    public required string FavoriteRemoved { get; init; }
    public required string FavoriteUpdateFailed { get; init; }
    public required string ImportAccountTitle { get; init; }
    public required string ImportAccountSubtitle { get; init; }
    public required string SourceVaultPassword { get; init; }
    public required string DestinationVaultPassword { get; init; }
    public required string ImportSelectedAccount { get; init; }
    public required string AccountImportedFormat { get; init; }
    public required string ImportSourceLocked { get; init; }
    public required string ImportWrongSourcePassword { get; init; }
    public required string ImportDuplicate { get; init; }
    public required string ImportInvalidAccount { get; init; }
    public required string ImportFailed { get; init; }
    public required string EnableQrHotkeys { get; init; }
    public required string ToggleQrHotkeys { get; init; }
    public required string PreviousAccountHotkey { get; init; }
    public required string NextAccountHotkey { get; init; }
    public required string QrHotkeysEnabledStatus { get; init; }
    public required string QrHotkeysDisabledStatus { get; init; }
    public required string ActiveAccount { get; init; }

    public static UiStrings For(AppLanguage language) => language == AppLanguage.Russian ? Russian : English;

    private static UiStrings English { get; } = new()
    {
        Dashboard = "Dashboard", Confirmations = "Confirmations", QrSignIn = "QR sign-in", CloudVault = "Cloud vault", Settings = "Settings",
        VaultStatus = "VAULT STATUS", LocalProtection = "Local protection", VaultReady = "Vault ready",
        DashboardTitle = "Steam Guard dashboard", DashboardSubtitle = "Codes, account health and fast approvals in one place",
        ScanQr = "Scan QR", AddAccount = "Import .maFile", Accounts = "ACCOUNTS", Reload = "Reload", SearchAccounts = "Search by account or SteamID",
        SteamGuardCode = "STEAM GUARD CODE", CopyCode = "Copy code", SelectedAccount = "SELECTED ACCOUNT", NoAccountSelected = "No account selected",
        Favorite = "Favorite", SessionLive = "Session live", VaultLinked = "Vault linked", LoginAgain = "Login again", EndSessions = "End sessions",
        Security = "SECURITY", ProtectedLocally = "Protected locally", SecurityDescription = "Sensitive actions require an unlocked vault.",
        CloudSync = "CLOUD SYNC", ReadyToConnect = "Ready to connect", CloudDescription = "WebDAV and trusted-device sync in one protected vault.",
        SettingsSubtitle = "Appearance, security and account behavior", BackToDashboard = "Back to dashboard", Appearance = "APPEARANCE",
        Theme = "Interface theme", Language = "LANGUAGE", InterfaceLanguage = "Interface language", AccountPreferences = "ACCOUNT PREFERENCES",
        EnableSearch = "Enable account search", FavoritesFirst = "Show favorites first", Save = "Save settings", PreferencesSaved = "Settings saved",
        CopySteamId = "Copy SteamID", UnlockVault = "Unlock vault", VaultPassword = "Vault password", TryAgain = "Try again",
        VaultLockedTitle = "Vault locked", WrongPasswordTitle = "Wrong vault password", VaultMissingTitle = "Vault not found", VaultAttentionTitle = "Vault needs attention",
        VaultLockedDetail = "Enter the existing SDA++ encryption password to load accounts.", WrongPasswordDetail = "The password did not decrypt this vault. Your files were not changed.",
        VaultMissingDetail = "Place maFiles next to the application or set SDAPP_MAFILES.", AccountsReadyFormat = "{0} accounts ready", AccountsPartialFormat = "{0} ready, {1} skipped",
        EncryptedVault = "Encrypted vault", VaultRemainsLocked = "Vault remains locked", NoLocalVault = "No local vault", VaultUnavailable = "Vault unavailable",
        CloudTitle = "Cloud vault", CloudSubtitle = "Connect and inspect your encrypted backup before importing it", Provider = "Provider",
        WebDavEndpoint = "WebDAV URL", Username = "Username", Password = "Password", RemoteFolder = "Cloud folder",
        TestConnection = "Test connection", PullPreview = "Preview pull", SaveProfile = "Save profile",
        PasswordSessionOnly = "The password is kept in memory only for this request and is cleared afterwards.",
        CloudConnected = "WebDAV connection established.", CloudPreviewFoundFormat = "Accounts: {0} | Encrypted: {1} | Manifest: {2}",
        Yes = "yes", No = "no", FallbackListing = "fallback listing",
        PreviewSafety = "Pull preview never writes to the local vault. Import requires a separate confirmation and backup step.",
        PrepareImport = "Prepare import", ConfirmImport = "Import and create backup", Cancel = "Cancel",
        ImportWarningFormat = "Ready to import {0} accounts. The current vault will be backed up before any files are replaced.",
        ImportCompleteFormat = "Imported {0} accounts. Backup: {1}",
        QrTitle = "Steam QR sign-in", QrSubtitle = "Approve a Steam sign-in using the selected account",
        QrSelectedAccount = "APPROVE AS", ScanScreen = "Scan screen and approve", QrReady = "Ready to scan when the Steam QR code is visible.",
        QrScanning = "Scanning the visible desktop for a QR code...", QrApproving = "Steam QR found. Requesting approval...",
        QrApproved = "Sign-in approved successfully.", QrNotFound = "No QR code was found. Keep the Steam QR visible and try again.",
        QrUnsupported = "The detected QR is not a supported Steam sign-in code.", QrNoAccount = "Select an account before scanning.",
        QrNoSession = "This account has no mobile web session. Log in again first.", QrExpiredSession = "This account session has expired. Log in again first.",
        QrMissingSecret = "This account does not contain a Steam Guard shared secret.", QrUnsupportedPlatform = "Screen scanning is currently available on Windows only.",
        QrRejected = "Steam did not approve this sign-in. Refresh the QR code and try again.",
        QrPrivacy = "The screenshot stays in memory and is discarded immediately. Scanning starts only when you press the button.",
        QrHowTo = "Open the Steam sign-in QR on the screen, keep it visible, choose the account, then start scanning.",
        ConfirmationsTitle = "Confirmations", ConfirmationsSubtitle = "Review pending actions from every available account",
        RefreshConfirmations = "Refresh all accounts", LoadingConfirmations = "Checking account confirmations...",
        NoConfirmationsFormat = "Nothing pending. Accounts checked: {0}; expired: {1}; unavailable: {2}.",
        ConfirmationsLoadedFormat = "Pending: {0}. Accounts checked: {1}; expired: {2}; unavailable: {3}.",
        ConfirmationsLoadFailed = "Confirmations could not be loaded. No actions were performed.", Accept = "Accept", Reject = "Reject",
        ConfirmDecision = "Confirm action", AcceptConfirmationPromptFormat = "Accept “{0}” for account {1}?",
        RejectConfirmationPromptFormat = "Reject “{0}” for account {1}?", ConfirmationActionSucceeded = "Steam confirmation updated successfully.",
        ConfirmationActionFailed = "Steam did not complete this action. Refresh the list and try again.",
        ConfirmationSafety = "Nothing is accepted automatically. Accept and Reject always require a separate confirmation step.",
        QrCaptureSettings = "QR CAPTURE", QrCaptureSource = "Capture source", QrFullDesktop = "Full desktop",
        QrMonitorUnderCursor = "Monitor under cursor", QrAreaAroundCursor = "Area around cursor", QrCursorAreaSize = "Cursor area size",
        GlobalHotkeys = "GLOBAL HOTKEYS", EnableGlobalHotkeys = "Enable system-wide hotkeys", ScanQrHotkey = "Scan and approve QR",
        ConfirmationsHotkey = "Open confirmations", HotkeysActive = "Global hotkeys are active.", HotkeysDisabled = "Global hotkeys are disabled.",
        HotkeysInvalid = "Use Ctrl, Shift, or Alt plus a supported key.", HotkeysDuplicate = "The two actions cannot use the same hotkey.",
        HotkeysAlreadyInUse = "A hotkey is already used by another application. Previous bindings remain active.",
        HotkeysUnavailable = "Global hotkeys are unavailable on this system.",
        SteamLoginTitle = "Sign in to Steam", SteamLoginSubtitle = "Refresh the selected account session without storing your Steam password.",
        SteamPassword = "Steam password", LoginAndSave = "Sign in and save session", LoginInProgress = "Connecting securely to Steam...",
        LoginSucceeded = "Steam session refreshed and saved.", LoginFailed = "Steam sign-in failed. No vault files were changed.",
        LoginMissingAccount = "Select an account before signing in.", LoginVaultPasswordHint = "Required only when the local vault is encrypted.",
        LoginEmailCodeRequired = "Steam requested an email verification code.", EmailCode = "Email verification code",
        SubmitEmailCode = "Continue", EmailCodePromptFormat = "Enter the code sent to {0}.",
        EndSessionsTitle = "End all Steam sessions", EndSessionsWarningFormat = "This signs {0} out on every device, including SDA++. This action cannot be undone.",
        ConfirmEndSessions = "End all sessions", EndingSessions = "Asking Steam to end all sessions...",
        SessionsEnded = "All Steam sessions were ended. Use Login again to restore SDA++.",
        SessionsEndFailed = "Steam did not confirm session termination. No local session was removed.",
        SessionsMissing = "This account has no valid Steam session. Use Login again first.",
        SessionsEndedSaveFailed = "Steam ended the sessions, but SDA++ could not persist the local signed-out state. Reload the vault before continuing.",
        RemoveFavorite = "Remove favorite", FavoriteAdded = "Account added to favorites.", FavoriteRemoved = "Account removed from favorites.",
        FavoriteUpdateFailed = "The favorite setting could not be saved.",
        ImportAccountTitle = "Import Steam Guard account", ImportAccountSubtitle = "Import one existing .maFile into this vault. Existing accounts are never overwritten.",
        SourceVaultPassword = "Source vault password", DestinationVaultPassword = "Current vault password", ImportSelectedAccount = "Import account",
        AccountImportedFormat = "Account {0} imported successfully.", ImportSourceLocked = "Enter the password of the source encrypted vault.",
        ImportWrongSourcePassword = "The source vault password is incorrect.", ImportDuplicate = "This account already exists in the current vault.",
        ImportInvalidAccount = "The selected file is not a valid .maFile. Encrypted files also need their matching manifest.json beside them.",
        ImportFailed = "The account could not be imported. No existing account was changed.",
        EnableQrHotkeys = "Enable QR actions", ToggleQrHotkeys = "Toggle QR actions", PreviousAccountHotkey = "Previous account",
        NextAccountHotkey = "Next account", QrHotkeysEnabledStatus = "QR hotkeys enabled", QrHotkeysDisabledStatus = "QR hotkeys disabled",
        ActiveAccount = "Active account"
    };

    private static UiStrings Russian { get; } = new()
    {
        Dashboard = "Главная", Confirmations = "Подтверждения", QrSignIn = "QR-вход", CloudVault = "Облачное хранилище", Settings = "Настройки",
        VaultStatus = "СОСТОЯНИЕ ХРАНИЛИЩА", LocalProtection = "Локальная защита", VaultReady = "Хранилище готово",
        DashboardTitle = "Панель Steam Guard", DashboardSubtitle = "Коды, состояние аккаунтов и быстрые подтверждения",
        ScanQr = "Сканировать QR", AddAccount = "Импорт .maFile", Accounts = "АККАУНТЫ", Reload = "Обновить", SearchAccounts = "Поиск по аккаунту или SteamID",
        SteamGuardCode = "КОД STEAM GUARD", CopyCode = "Копировать код", SelectedAccount = "ВЫБРАННЫЙ АККАУНТ", NoAccountSelected = "Аккаунт не выбран",
        Favorite = "Избранное", SessionLive = "Сессия активна", VaultLinked = "Хранилище подключено", LoginAgain = "Войти снова", EndSessions = "Завершить сессии",
        Security = "БЕЗОПАСНОСТЬ", ProtectedLocally = "Локальная защита", SecurityDescription = "Важные действия требуют разблокированного хранилища.",
        CloudSync = "ОБЛАЧНАЯ СИНХРОНИЗАЦИЯ", ReadyToConnect = "Готово к подключению", CloudDescription = "WebDAV и доверенные устройства работают с единым защищённым хранилищем.",
        SettingsSubtitle = "Оформление, безопасность и поведение аккаунтов", BackToDashboard = "Вернуться на главную", Appearance = "ОФОРМЛЕНИЕ",
        Theme = "Тема интерфейса", Language = "ЯЗЫК", InterfaceLanguage = "Язык интерфейса", AccountPreferences = "НАСТРОЙКИ АККАУНТОВ",
        EnableSearch = "Включить поиск аккаунтов", FavoritesFirst = "Показывать избранные сверху", Save = "Сохранить настройки", PreferencesSaved = "Настройки сохранены",
        CopySteamId = "Копировать SteamID", UnlockVault = "Разблокировать", VaultPassword = "Пароль хранилища", TryAgain = "Повторить",
        VaultLockedTitle = "Хранилище заблокировано", WrongPasswordTitle = "Неверный пароль хранилища", VaultMissingTitle = "Хранилище не найдено", VaultAttentionTitle = "Требуется проверка хранилища",
        VaultLockedDetail = "Введите существующий пароль шифрования SDA++, чтобы загрузить аккаунты.", WrongPasswordDetail = "Пароль не подошёл. Файлы хранилища не были изменены.",
        VaultMissingDetail = "Поместите maFiles рядом с приложением или задайте SDAPP_MAFILES.", AccountsReadyFormat = "Аккаунтов готово: {0}", AccountsPartialFormat = "Готово: {0}, пропущено: {1}",
        EncryptedVault = "Зашифрованное хранилище", VaultRemainsLocked = "Хранилище осталось заблокированным", NoLocalVault = "Локальное хранилище отсутствует", VaultUnavailable = "Хранилище недоступно",
        CloudTitle = "Облачное хранилище", CloudSubtitle = "Подключение и проверка зашифрованной копии перед импортом", Provider = "Провайдер",
        WebDavEndpoint = "URL WebDAV", Username = "Логин", Password = "Пароль", RemoteFolder = "Папка в облаке",
        TestConnection = "Проверить соединение", PullPreview = "Проверить загрузку", SaveProfile = "Сохранить профиль",
        PasswordSessionOnly = "Пароль хранится в памяти только во время запроса и сразу после него очищается.",
        CloudConnected = "Соединение с WebDAV установлено.", CloudPreviewFoundFormat = "Аккаунтов: {0} | Зашифровано: {1} | Manifest: {2}",
        Yes = "да", No = "нет", FallbackListing = "список файлов",
        PreviewSafety = "Предпросмотр не изменяет локальное хранилище. Импорт потребует отдельного подтверждения и создания резервной копии.",
        PrepareImport = "Подготовить импорт", ConfirmImport = "Импортировать и создать копию", Cancel = "Отмена",
        ImportWarningFormat = "Готово к импорту аккаунтов: {0}. Перед заменой файлов будет создана резервная копия текущего хранилища.",
        ImportCompleteFormat = "Импортировано аккаунтов: {0}. Резервная копия: {1}",
        QrTitle = "QR-вход Steam", QrSubtitle = "Подтверждение входа Steam через выбранный аккаунт",
        QrSelectedAccount = "ПОДТВЕРДИТЬ ОТ ИМЕНИ", ScanScreen = "Сканировать экран и подтвердить", QrReady = "Готово к сканированию, когда QR-код Steam виден на экране.",
        QrScanning = "Поиск QR-кода на видимом рабочем столе...", QrApproving = "QR-код Steam найден. Выполняется подтверждение...",
        QrApproved = "Вход успешно подтверждён.", QrNotFound = "QR-код не найден. Оставьте QR Steam видимым и повторите попытку.",
        QrUnsupported = "Обнаруженный QR-код не является поддерживаемым кодом входа Steam.", QrNoAccount = "Перед сканированием выберите аккаунт.",
        QrNoSession = "У аккаунта нет мобильной веб-сессии. Сначала войдите повторно.", QrExpiredSession = "Сессия аккаунта истекла. Сначала войдите повторно.",
        QrMissingSecret = "В аккаунте отсутствует shared secret Steam Guard.", QrUnsupportedPlatform = "Сканирование экрана пока доступно только в Windows.",
        QrRejected = "Steam не подтвердил вход. Обновите QR-код и повторите попытку.",
        QrPrivacy = "Снимок остаётся только в памяти и сразу удаляется. Сканирование запускается исключительно по нажатию кнопки.",
        QrHowTo = "Откройте QR входа Steam на экране, оставьте его видимым, выберите аккаунт и запустите сканирование.",
        ConfirmationsTitle = "Подтверждения", ConfirmationsSubtitle = "Ожидающие действия со всех доступных аккаунтов",
        RefreshConfirmations = "Обновить все аккаунты", LoadingConfirmations = "Проверка подтверждений аккаунтов...",
        NoConfirmationsFormat = "Ожидающих действий нет. Проверено: {0}; сессия истекла: {1}; недоступно: {2}.",
        ConfirmationsLoadedFormat = "Ожидает: {0}. Проверено: {1}; сессия истекла: {2}; недоступно: {3}.",
        ConfirmationsLoadFailed = "Не удалось загрузить подтверждения. Никакие действия не выполнены.", Accept = "Принять", Reject = "Отклонить",
        ConfirmDecision = "Подтвердить действие", AcceptConfirmationPromptFormat = "Принять «{0}» для аккаунта {1}?",
        RejectConfirmationPromptFormat = "Отклонить «{0}» для аккаунта {1}?", ConfirmationActionSucceeded = "Подтверждение Steam успешно обработано.",
        ConfirmationActionFailed = "Steam не выполнил действие. Обновите список и повторите попытку.",
        ConfirmationSafety = "Ничего не принимается автоматически. Принятие и отклонение всегда требуют отдельного подтверждения.",
        QrCaptureSettings = "ЗАХВАТ QR", QrCaptureSource = "Источник захвата", QrFullDesktop = "Весь рабочий стол",
        QrMonitorUnderCursor = "Монитор под курсором", QrAreaAroundCursor = "Область вокруг курсора", QrCursorAreaSize = "Размер области у курсора",
        GlobalHotkeys = "ГЛОБАЛЬНЫЕ СОЧЕТАНИЯ", EnableGlobalHotkeys = "Включить системные сочетания клавиш", ScanQrHotkey = "Сканировать и подтвердить QR",
        ConfirmationsHotkey = "Открыть подтверждения", HotkeysActive = "Глобальные сочетания активны.", HotkeysDisabled = "Глобальные сочетания отключены.",
        HotkeysInvalid = "Используйте Ctrl, Shift или Alt вместе с поддерживаемой клавишей.", HotkeysDuplicate = "Для двух действий нельзя назначить одинаковое сочетание.",
        HotkeysAlreadyInUse = "Сочетание уже занято другой программой. Предыдущие назначения сохранены.",
        HotkeysUnavailable = "Глобальные сочетания недоступны в этой системе.",
        SteamLoginTitle = "Вход в Steam", SteamLoginSubtitle = "Обновление сессии выбранного аккаунта без сохранения пароля Steam.",
        SteamPassword = "Пароль Steam", LoginAndSave = "Войти и сохранить сессию", LoginInProgress = "Безопасное подключение к Steam...",
        LoginSucceeded = "Сессия Steam обновлена и сохранена.", LoginFailed = "Не удалось войти в Steam. Файлы хранилища не изменены.",
        LoginMissingAccount = "Перед входом выберите аккаунт.", LoginVaultPasswordHint = "Требуется только для зашифрованного локального хранилища.",
        LoginEmailCodeRequired = "Steam запросил код подтверждения из email.", EmailCode = "Код подтверждения из email",
        SubmitEmailCode = "Продолжить", EmailCodePromptFormat = "Введите код, отправленный на {0}.",
        EndSessionsTitle = "Завершение всех сессий Steam", EndSessionsWarningFormat = "Аккаунт {0} будет выведен со всех устройств, включая SDA++. Это действие нельзя отменить.",
        ConfirmEndSessions = "Завершить все сессии", EndingSessions = "Steam получает запрос на завершение всех сессий...",
        SessionsEnded = "Все сессии Steam завершены. Используйте «Войти снова», чтобы восстановить SDA++.",
        SessionsEndFailed = "Steam не подтвердил завершение сессий. Локальная сессия не удалена.",
        SessionsMissing = "У аккаунта нет действующей сессии Steam. Сначала используйте «Войти снова».",
        SessionsEndedSaveFailed = "Steam завершил сессии, но SDA++ не смог сохранить локальное состояние выхода. Перезагрузите хранилище перед продолжением.",
        RemoveFavorite = "Убрать из избранного", FavoriteAdded = "Аккаунт добавлен в избранное.", FavoriteRemoved = "Аккаунт убран из избранного.",
        FavoriteUpdateFailed = "Не удалось сохранить настройку избранного.",
        ImportAccountTitle = "Импорт аккаунта Steam Guard", ImportAccountSubtitle = "Импорт одного существующего .maFile в текущее хранилище. Существующие аккаунты не перезаписываются.",
        SourceVaultPassword = "Пароль исходного хранилища", DestinationVaultPassword = "Пароль текущего хранилища", ImportSelectedAccount = "Импортировать аккаунт",
        AccountImportedFormat = "Аккаунт {0} успешно импортирован.", ImportSourceLocked = "Введите пароль исходного зашифрованного хранилища.",
        ImportWrongSourcePassword = "Неверный пароль исходного хранилища.", ImportDuplicate = "Этот аккаунт уже существует в текущем хранилище.",
        ImportInvalidAccount = "Выбранный файл не является корректным .maFile. Для зашифрованного файла рядом также нужен соответствующий manifest.json.",
        ImportFailed = "Не удалось импортировать аккаунт. Существующие аккаунты не изменены.",
        EnableQrHotkeys = "Включить действия QR", ToggleQrHotkeys = "Включить или отключить QR", PreviousAccountHotkey = "Предыдущий аккаунт",
        NextAccountHotkey = "Следующий аккаунт", QrHotkeysEnabledStatus = "QR-хоткеи включены", QrHotkeysDisabledStatus = "QR-хоткеи отключены",
        ActiveAccount = "Активный аккаунт"
    };
}
