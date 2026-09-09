using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using SDAPlusPlus.Core.Devices;

namespace SDAPlusPlus.Core.Tests.Devices;

public sealed class WindowsDeviceRegistryStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "sdapp-registry-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void Load_ReturnsMissing_WhenRegistryDoesNotExist()
    {
        DeviceRegistryLoadResult result = new WindowsDeviceRegistryStore(_directory).Load();

        Assert.Equal(DeviceRegistryLoadState.Missing, result.State);
        Assert.False(result.Settings.IsConfigured);
    }

    [Fact]
    public void Load_ReadsLegacyDpapiRegistry()
    {
        if (!OperatingSystem.IsWindows())
            return;

        Directory.CreateDirectory(_directory);
        DeviceRegistrySettings expected = new()
        {
            VaultId = "vault-test",
            RelayUrl = "https://relay.example",
            Role = "TrustedOnline",
            GrantedAccountSteamIds = ["76561198000000000"]
        };
        byte[] plain = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(expected));
        byte[] entropy = Encoding.UTF8.GetBytes("SDA++ desktop device registry v1");
        try
        {
            File.WriteAllBytes(
                Path.Combine(_directory, "device-registry.secure"),
                ProtectedData.Protect(plain, entropy, DataProtectionScope.CurrentUser));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plain);
        }

        DeviceRegistryLoadResult result = new WindowsDeviceRegistryStore(_directory).Load();

        Assert.Equal(DeviceRegistryLoadState.Ready, result.State);
        Assert.Equal("TrustedOnline", result.Settings.Role);
        Assert.Contains("76561198000000000", result.Settings.GrantedAccountSteamIds);
    }

    [Fact]
    public void Save_RoundTripsCompatibleRegistry()
    {
        if (!OperatingSystem.IsWindows())
            return;

        WindowsDeviceRegistryStore store = new(_directory);
        store.Save(new DeviceRegistrySettings
        {
            VaultId = "vault-saved",
            RelayUrl = "https://relay.example",
            Role = "Admin",
            AdminPinIterations = 210000
        });

        DeviceRegistryLoadResult result = store.Load();

        Assert.Equal(DeviceRegistryLoadState.Ready, result.State);
        Assert.Equal("vault-saved", result.Settings.VaultId);
        Assert.Equal(210000, result.Settings.AdminPinIterations);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, true);
    }
}
