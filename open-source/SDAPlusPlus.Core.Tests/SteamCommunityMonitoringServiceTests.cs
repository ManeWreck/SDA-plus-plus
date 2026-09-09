using SDAPlusPlus.Core.Monitoring;
using SDAPlusPlus.Core.Vault;
using SteamAuth;

namespace SDAPlusPlus.Core.Tests;

public sealed class SteamCommunityMonitoringServiceTests
{
    [Fact]
    public async Task LoadSnapshotAsync_ReturnsSafeMissingSteamIdStateWithoutNetworkRequest()
    {
        var account = new VaultAccount(
            "test-account",
            "SteamID unavailable",
            new SteamGuardAccount { AccountName = "test-account", SharedSecret = Convert.ToBase64String(new byte[20]) },
            false,
            false);
        var service = new SteamCommunityMonitoringService();

        AccountMonitoringSnapshot result = await service.LoadSnapshotAsync(account);

        Assert.Equal(MonitoredSessionHealth.Missing, result.SessionHealth);
        Assert.Equal(MonitoredInventoryState.Unavailable, result.InventoryState);
        Assert.Contains("SteamID", result.Error, StringComparison.Ordinal);
    }
}
