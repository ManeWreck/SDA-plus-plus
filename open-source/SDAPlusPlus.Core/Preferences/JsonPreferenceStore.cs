using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using SDAPlusPlus.Core.Qr;
using SDAPlusPlus.Core.Hotkeys;

namespace SDAPlusPlus.Core.Preferences;

public sealed class JsonPreferenceStore(string filePath) : IPreferenceStore
{
    private static readonly JsonSerializerSettings SerializerSettings = new()
    {
        Formatting = Formatting.Indented,
        Converters = [new StringEnumConverter()]
    };

    public string FilePath { get; } = Path.GetFullPath(filePath);

    public AppPreferences Load()
    {
        if (!File.Exists(FilePath))
        {
            return new AppPreferences();
        }

        try
        {
            AppPreferences? preferences = JsonConvert.DeserializeObject<AppPreferences>(File.ReadAllText(FilePath), SerializerSettings);
            return Normalize(preferences ?? new AppPreferences());
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return new AppPreferences();
        }
    }

    public void Save(AppPreferences preferences)
    {
        ArgumentNullException.ThrowIfNull(preferences);
        string? directory = Path.GetDirectoryName(FilePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        string temporaryPath = FilePath + ".tmp";
        string json = JsonConvert.SerializeObject(Normalize(preferences), SerializerSettings);
        File.WriteAllText(temporaryPath, json);
        File.Move(temporaryPath, FilePath, overwrite: true);
    }

    public static string GetDefaultPath()
    {
        string localData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(localData))
        {
            localData = AppContext.BaseDirectory;
        }

        return Path.Combine(localData, "SDAPlusPlus", "preferences.json");
    }

    private static AppPreferences Normalize(AppPreferences preferences) => preferences with
    {
        Language = Enum.IsDefined(preferences.Language) ? preferences.Language : AppLanguage.English,
        Theme = Enum.IsDefined(preferences.Theme) ? preferences.Theme : ThemePreset.Graphite,
        Density = Enum.IsDefined(preferences.Density) ? preferences.Density : UiDensity.Compact,
        CloudProvider = Enum.IsDefined(preferences.CloudProvider) ? preferences.CloudProvider : SDAPlusPlus.Core.Cloud.CloudProviderKind.WebDav,
        QrCaptureMode = Enum.IsDefined(preferences.QrCaptureMode) ? preferences.QrCaptureMode : QrCaptureMode.FullDesktop,
        QrCursorAreaSize = Math.Clamp(preferences.QrCursorAreaSize, 250, 2000),
        ConfirmationCheckIntervalSeconds = Math.Clamp(preferences.ConfirmationCheckIntervalSeconds, 5, 3600),
        QrToggleHotkey = NormalizeHotkey(preferences.QrToggleHotkey, "Ctrl+Shift+Q"),
        QrScanHotkey = NormalizeHotkey(preferences.QrScanHotkey, "Ctrl+Shift+S"),
        PreviousAccountHotkey = NormalizeHotkey(preferences.PreviousAccountHotkey, "Ctrl+Shift+Left"),
        NextAccountHotkey = NormalizeHotkey(preferences.NextAccountHotkey, "Ctrl+Shift+Right"),
        AutoLoginAllHotkey = NormalizeHotkey(preferences.AutoLoginAllHotkey, "Ctrl+Shift+L"),
        ConfirmationsHotkey = NormalizeHotkey(preferences.ConfirmationsHotkey, "Ctrl+Shift+C")
    };

    private static string NormalizeHotkey(string value, string fallback) =>
        HotkeyGestureParser.TryParse(value, out HotkeyGesture? gesture) ? gesture!.ToString() : fallback;
}
