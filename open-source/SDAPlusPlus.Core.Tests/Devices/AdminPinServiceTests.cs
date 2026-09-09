using SDAPlusPlus.Core.Devices;

namespace SDAPlusPlus.Core.Tests.Devices;

public sealed class AdminPinServiceTests
{
    [Fact]
    public void ConfigureAndVerify_UsesCompatibleParameters()
    {
        MemoryStore store = new();
        DeviceRegistrySettings settings = new() { Role = "Admin", VaultId = "vault" };
        AdminPinService service = new(store);

        AdminPinResult configured = service.Configure(settings, "123456", "123456");
        AdminPinResult verified = service.Verify(settings, "123456");

        Assert.Equal(AdminPinState.Success, configured.State);
        Assert.Equal(AdminPinState.Success, verified.State);
        Assert.Equal(AdminPinService.Iterations, settings.AdminPinIterations);
        Assert.True(store.SaveCount >= 2);
    }

    [Fact]
    public void Verify_LocksAfterFiveWrongAttempts()
    {
        MemoryStore store = new();
        DeviceRegistrySettings settings = new();
        AdminPinService service = new(store);
        service.Configure(settings, "123456", "123456");

        for (int attempt = 0; attempt < 5; attempt++)
            Assert.Equal(AdminPinState.WrongPin, service.Verify(settings, "654321").State);

        AdminPinResult locked = service.Verify(settings, "123456");
        Assert.Equal(AdminPinState.Locked, locked.State);
        Assert.InRange(locked.RetryAfterSeconds, 1, 30);
    }

    [Theory]
    [InlineData("12345")]
    [InlineData("1234567890123")]
    [InlineData("abcdef")]
    public void Configure_RejectsInvalidPin(string pin)
    {
        AdminPinResult result = new AdminPinService(new MemoryStore()).Configure(new DeviceRegistrySettings(), pin, pin);

        Assert.Equal(AdminPinState.InvalidFormat, result.State);
    }

    private sealed class MemoryStore : IDeviceRegistryStore
    {
        public int SaveCount { get; private set; }
        public DeviceRegistryLoadResult Load() => new(DeviceRegistryLoadState.Missing, new DeviceRegistrySettings());
        public void Save(DeviceRegistrySettings settings) => SaveCount++;
    }
}
