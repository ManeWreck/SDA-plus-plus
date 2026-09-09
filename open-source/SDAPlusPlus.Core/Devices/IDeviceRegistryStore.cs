namespace SDAPlusPlus.Core.Devices;

public interface IDeviceRegistryStore
{
    DeviceRegistryLoadResult Load();
    void Save(DeviceRegistrySettings settings);
}
