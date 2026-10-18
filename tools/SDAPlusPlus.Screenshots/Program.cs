using System.Reflection;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Threading;
using SDAPlusPlus.Core.Cloud;
using SDAPlusPlus.Core.Confirmations;
using SDAPlusPlus.Core.Credentials;
using SDAPlusPlus.Core.Devices;
using SDAPlusPlus.Core.Monitoring;
using SDAPlusPlus.Core.Preferences;
using SDAPlusPlus.Core.Qr;
using SDAPlusPlus.Core.Sessions;
using SDAPlusPlus.Core.Vault;
using SDAPlusPlus.Desktop.Services;
using SDAPlusPlus.Desktop.ViewModels;
using SDAPlusPlus.Desktop.Views;
using SteamAuth;

// Actual application views rendered by Skia. All services fail closed; no user profile or network.
AppBuilder.Configure<SDAPlusPlus.Desktop.App>().UseSkia().WithInterFont()
    .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
string output = Path.GetFullPath(args.FirstOrDefault() ?? "docs/screenshots");
Directory.CreateDirectory(output);
T Stub<T>() where T : class => DispatchProxy.Create<T, DemoServices>();
using var model = new MainViewModel(
    Stub<ILegacyVaultReader>(), Stub<IPreferenceStore>(), Stub<IClipboardService>(),
    Stub<ICloudVaultProvider>(), Stub<IVaultImportService>(), Stub<IScreenQrScanner>(),
    Stub<ISteamQrApprovalService>(), Stub<IConfirmationService>(), Stub<IGlobalHotkeyService>(),
    Stub<ISteamSessionLoginService>(), Stub<IVaultAccountWriter>(), Stub<ISteamSessionTerminationService>(),
    Stub<IVaultAccountImportService>(), Stub<IAccountFilePicker>(), Stub<IHotkeyOverlayService>(),
    "DEMO / no local account files", Stub<IAccountMonitoringService>(), Stub<IStoredCredentialVault>(),
    Stub<IVaultEncryptionService>(), Stub<IDeviceRegistryStore>(), Stub<IPairingRelayService>(),
    Stub<IDeviceRegistryClient>(), Stub<IQrCodeRenderer>());
for (int i = 1; i <= 3; i++)
{
    var account = new VaultAccount(new[] { "Demo Explorer", "Demo Workshop", "Demo Collector" }[i - 1],
        $"DEMO-00{i}", new SteamGuardAccount { AccountName = $"demo_{i}", SharedSecret = Convert.ToBase64String(new byte[20]) }, false, i == 1);
    model.Accounts.Add(new AccountItemViewModel(account, false));
}
model.SelectedAccount = model.Accounts[0];
model.StatusMessage = "3 fictional accounts · documentation preview";
model.WebDavEndpoint = "https://webdav.example.com/";
model.WebDavUsername = "demo-user";
model.WebDavRemotePath = "SDA-Demo";
model.CloudStatus = "Demo profile — no network connection";
model.CloudPairingRelay = "https://relay.example.com";
var window = new MainWindow { DataContext = model, Width = 1440, Height = 1000, Title = "SDA++ · documentation demo" };
window.Show();
void Capture(string name)
{
    Dispatcher.UIThread.RunJobs();
    using var frame = window.CaptureRenderedFrame() ?? throw new InvalidOperationException("No rendered frame.");
    frame.Save(Path.Combine(output, name + ".png"));
    Console.WriteLine(name);
}
Capture("dashboard");
model.ShowQrCommand.Execute(null);
Capture("qr-sign-in");
model.ShowCloudCommand.Execute(null);
Capture("cloud");
model.IsCloudPairingVisible = true;
Capture("mobile-pairing");
model.CloseCloudPairingCommand.Execute(null);
model.ShowCredentialsCommand.Execute(null);
foreach (var account in model.Accounts) model.CredentialAccounts.Add(new AccountCredentialRowViewModel(account, null, false));
Capture("credentials");
foreach (var account in model.Accounts)
    model.MonitoringAccounts.Add(new MonitoringItemViewModel(new AccountMonitoringSnapshot(
        account.Account, MonitoredSessionHealth.Missing, "Clear", 10, 24, MonitoredInventoryState.Available, 12,
        false, new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero), IsCached: true), AppLanguage.English));
model.ShowMonitoringCommand.Execute(null);
model.MonitoringStatus = "Fictional demonstration values — no Steam requests";
Capture("monitoring");
model.ShowSettingsCommand.Execute(null);
Capture("settings");
model.SelectedLanguageIndex = 1;
model.ShowDashboardCommand.Execute(null);
model.StatusMessage = "Демонстрационные аккаунты";
foreach (var account in model.Accounts) account.SetLanguage(true);
Capture("dashboard-ru");
window.Width = 920; window.Height = 720;
Capture("dashboard-ru-compact");
model.SelectedAccount = null;
model.ShowQrCommand.Execute(null);
Capture("qr-empty-ru");
window.Close();

public class DemoServices : DispatchProxy
{
    protected override object? Invoke(MethodInfo? method, object?[]? args)
    {
        if (method is null) throw new InvalidOperationException();
        if (method.Name.StartsWith("add_") || method.Name.StartsWith("remove_") || method.Name == "Dispose") return null;
        if (method.DeclaringType == typeof(IGlobalHotkeyService) && method.Name == "Configure") return new HotkeyRegistrationResult(true);
        if (method.Name == "Load")
        {
            if (method.DeclaringType == typeof(IPreferenceStore)) return new AppPreferences
            { InitialSetupCompleted = true, CheckUpdatesOnStartup = false, GlobalHotkeysEnabled = false, PeriodicConfirmationChecking = false };
            if (method.DeclaringType == typeof(ILegacyVaultReader)) return new VaultLoadResult(VaultLoadState.Ready, [], "DEMO");
            if (method.DeclaringType == typeof(IDeviceRegistryStore)) return new DeviceRegistryLoadResult(DeviceRegistryLoadState.Missing, new());
            if (method.DeclaringType == typeof(IStoredCredentialVault)) return new CredentialVaultLoadResult(CredentialVaultState.Missing, [], "DEMO / credentials.secure.json", false);
        }
        if (method.DeclaringType == typeof(IStoredCredentialVault) && method.Name == "TryGet") return false;
        throw new InvalidOperationException($"Documentation rendering attempted forbidden service call: {method.DeclaringType?.Name}.{method.Name}");
    }
}
