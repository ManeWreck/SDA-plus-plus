using SDAPlusPlus.Core.Preferences;
using SDAPlusPlus.Core.Qr;

namespace SDAPlusPlus.Core.Tests;

public sealed class JsonPreferenceStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "sdapp-preference-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void SavesAndLoadsUiPreferencesWithoutSecretFields()
    {
        string path = Path.Combine(_directory, "preferences.json");
        var store = new JsonPreferenceStore(path);
        var expected = new AppPreferences
        {
            Language = AppLanguage.Russian,
            Theme = ThemePreset.Obsidian,
            Density = UiDensity.Comfortable,
            AccountSearchEnabled = false,
            FavoritesFirst = false,
            PeriodicConfirmationChecking = true,
            ConfirmationCheckIntervalSeconds = 45,
            CheckAllAccountsForConfirmations = true,
            AutoConfirmMarketTransactions = true,
            AutoConfirmGiftTrades = true,
            QrCaptureMode = QrCaptureMode.AreaAroundCursor,
            QrCursorAreaSize = 850
        };

        store.Save(expected);
        AppPreferences actual = store.Load();
        string json = File.ReadAllText(path);

        Assert.Equal(expected, actual);
        Assert.DoesNotContain("password", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("token", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("secret", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void UnsafeConfirmationIntervalIsNormalized()
    {
        string path = Path.Combine(_directory, "preferences.json");
        Directory.CreateDirectory(_directory);
        File.WriteAllText(path, "{\"ConfirmationCheckIntervalSeconds\":1}");

        AppPreferences result = new JsonPreferenceStore(path).Load();

        Assert.Equal(5, result.ConfirmationCheckIntervalSeconds);
    }

    [Fact]
    public void UnsafeQrAreaSettingsAreNormalized()
    {
        string path = Path.Combine(_directory, "preferences.json");
        Directory.CreateDirectory(_directory);
        File.WriteAllText(path, "{\"QrCaptureMode\":999,\"QrCursorAreaSize\":9000}");

        AppPreferences result = new JsonPreferenceStore(path).Load();

        Assert.Equal(QrCaptureMode.FullDesktop, result.QrCaptureMode);
        Assert.Equal(2000, result.QrCursorAreaSize);
    }

    [Fact]
    public void CorruptPreferencesFallBackToSafeDefaults()
    {
        Directory.CreateDirectory(_directory);
        string path = Path.Combine(_directory, "preferences.json");
        File.WriteAllText(path, "not-json");

        AppPreferences result = new JsonPreferenceStore(path).Load();

        Assert.Equal(new AppPreferences(), result);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }
}
