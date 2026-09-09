using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;

namespace SDAPlusPlus.Core.Devices;

public sealed class WindowsDeviceRegistryStore(string maFilesDirectory) : IDeviceRegistryStore
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("SDA++ desktop device registry v1");
    private readonly string _path = Path.Combine(maFilesDirectory, "device-registry.secure");

    public DeviceRegistryLoadResult Load()
    {
        if (!File.Exists(_path))
            return new DeviceRegistryLoadResult(DeviceRegistryLoadState.Missing, new DeviceRegistrySettings());

        if (!OperatingSystem.IsWindows())
            return new DeviceRegistryLoadResult(
                DeviceRegistryLoadState.UnsupportedPlatform,
                new DeviceRegistrySettings(),
                "The existing registry is protected by Windows DPAPI.");

        byte[]? plain = null;
        try
        {
            byte[] encrypted = File.ReadAllBytes(_path);
            plain = ProtectedData.Unprotect(encrypted, Entropy, DataProtectionScope.CurrentUser);
            DeviceRegistrySettings settings = JsonConvert.DeserializeObject<DeviceRegistrySettings>(Encoding.UTF8.GetString(plain))
                ?? new DeviceRegistrySettings();
            return new DeviceRegistryLoadResult(DeviceRegistryLoadState.Ready, settings);
        }
        catch (Exception ex) when (ex is CryptographicException or IOException or JsonException)
        {
            return new DeviceRegistryLoadResult(DeviceRegistryLoadState.Invalid, new DeviceRegistrySettings(), ex.Message);
        }
        finally
        {
            if (plain is not null)
                CryptographicOperations.ZeroMemory(plain);
        }
    }

    public void Save(DeviceRegistrySettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("The SDA++ device registry uses Windows DPAPI.");

        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        byte[] plain = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(settings));
        string temporaryPath = _path + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            byte[] encrypted = ProtectedData.Protect(plain, Entropy, DataProtectionScope.CurrentUser);
            File.WriteAllBytes(temporaryPath, encrypted);
            File.Move(temporaryPath, _path, true);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plain);
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }
    }
}
