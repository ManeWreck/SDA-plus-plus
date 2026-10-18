using SDAPlusPlus.Core.Qr;
using SDAPlusPlus.Core.Cloud;

namespace SDAPlusPlus.Core.Preferences;

public enum AppLanguage
{
    English,
    Russian
}

public enum ThemePreset
{
    Graphite,
    Obsidian,
    Slate
}

public enum UiDensity
{
    Compact,
    Comfortable
}

public sealed record AppPreferences
{
    public bool InitialSetupCompleted { get; init; }
    public bool CheckUpdatesOnStartup { get; init; } = true;
    public AppLanguage Language { get; init; } = AppLanguage.English;
    public ThemePreset Theme { get; init; } = ThemePreset.Graphite;
    public UiDensity Density { get; init; } = UiDensity.Compact;
    public bool AccountSearchEnabled { get; init; } = true;
    public bool FavoritesFirst { get; init; } = true;
    public bool PeriodicConfirmationChecking { get; init; }
    public int ConfirmationCheckIntervalSeconds { get; init; } = 30;
    public bool CheckAllAccountsForConfirmations { get; init; }
    public bool AutoConfirmMarketTransactions { get; init; }
    public bool AutoConfirmGiftTrades { get; init; }
    public QrCaptureMode QrCaptureMode { get; init; } = QrCaptureMode.FullDesktop;
    public int QrCursorAreaSize { get; init; } = 700;
    public bool GlobalHotkeysEnabled { get; init; } = true;
    public bool QrHotkeysEnabled { get; init; } = true;
    public string QrToggleHotkey { get; init; } = "Ctrl+Shift+Q";
    public string QrScanHotkey { get; init; } = "Ctrl+Shift+S";
    public string PreviousAccountHotkey { get; init; } = "Ctrl+Shift+Left";
    public string NextAccountHotkey { get; init; } = "Ctrl+Shift+Right";
    public string AutoLoginAllHotkey { get; init; } = "Ctrl+Shift+L";
    public string ConfirmationsHotkey { get; init; } = "Ctrl+Shift+C";
    public string WebDavEndpoint { get; init; } = string.Empty;
    public string WebDavUsername { get; init; } = string.Empty;
    public string WebDavRemotePath { get; init; } = "SDAppVault";
    public CloudProviderKind CloudProvider { get; init; } = CloudProviderKind.WebDav;
    public string S3Endpoint { get; init; } = string.Empty;
    public string S3Bucket { get; init; } = string.Empty;
    public string S3AccessKey { get; init; } = string.Empty;
    public string S3RemotePath { get; init; } = "SDAppVault";
    public string DropboxRemotePath { get; init; } = "SDAppVault";
    public string OneDriveClientId { get; init; } = string.Empty;
    public string OneDriveRemotePath { get; init; } = "SDAppVault";
    public string GoogleDriveClientId { get; init; } = string.Empty;
    public string GoogleDriveRemotePath { get; init; } = "SDAppVault";
}
