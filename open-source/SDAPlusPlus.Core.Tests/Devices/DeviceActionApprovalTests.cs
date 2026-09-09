using SDAPlusPlus.Core.Devices;

namespace SDAPlusPlus.Core.Tests.Devices;

public sealed class DeviceActionApprovalTests
{
    private static DeviceRegistrySettings Settings() => new() { VaultId = "vault", RelayUrl = "https://relay.example", Role = "Admin" };
    private static RegistryDevice Target() => new() { DeviceId = "phone-1", Role = "TrustedOnline" };
    private static DeviceActionApproval Approval() => new("vault", "https://relay.example", "phone-1", "WipeLocalData");

    [Fact]
    public void CapturedTargetCannotBeReplacedByAnotherSelection()
    {
        Assert.True(Approval().Allows(Settings(), Target()));
        var other = Target();
        other.DeviceId = "phone-2";
        Assert.False(Approval().Allows(Settings(), other));
        Assert.False(Approval().Allows(Settings(), null));
    }

    [Fact]
    public void ChangedPermissionsOrRegistryRejectConfirmation()
    {
        var settings = Settings();
        settings.Role = "TrustedOnline";
        Assert.False(Approval().Allows(settings, Target()));
        settings = Settings();
        settings.VaultId = "other";
        Assert.False(Approval().Allows(settings, Target()));
        settings = Settings();
        settings.RelayUrl = "https://other.example";
        Assert.False(Approval().Allows(settings, Target()));
        settings = Settings();
        settings.ExpiresAt = 1;
        Assert.False(Approval().Allows(settings, Target()));
    }

    [Fact]
    public void RevokedOrAdminTargetIsRejected()
    {
        var target = Target();
        target.RevokedAt = 1;
        Assert.False(Approval().Allows(Settings(), target));
        target = Target();
        target.Role = "Admin";
        Assert.False(Approval().Allows(Settings(), target));
    }

    [Fact]
    public void UnknownCommandIsRejected() =>
        Assert.False((Approval() with { Action = "unknown" }).Allows(Settings(), Target()));
}
