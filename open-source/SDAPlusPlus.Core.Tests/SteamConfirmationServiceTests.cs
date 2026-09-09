using SDAPlusPlus.Core.Confirmations;
using SDAPlusPlus.Core.Vault;
using SteamAuth;

namespace SDAPlusPlus.Core.Tests;

public sealed class SteamConfirmationServiceTests
{
    [Fact]
    public async Task AccountsWithoutSessionsAreSkippedWithoutNetworkCalls()
    {
        var guard = new SteamGuardAccount { AccountName = "offline", Session = null };
        var account = new VaultAccount("offline", "76561198000000000", guard, false, false);

        ConfirmationLoadResult result = await new SteamConfirmationService().LoadAsync([account]);

        Assert.Empty(result.Items);
        Assert.Equal(1, result.AccountsChecked);
        Assert.Equal(1, result.ExpiredAccounts);
        Assert.Equal(0, result.FailedAccounts);
        Assert.Contains("offline: no mobile web session", result.FailureDetails!);
    }

    [Fact]
    public async Task EmptyAccountSetReturnsEmptyResult()
    {
        ConfirmationLoadResult result = await new SteamConfirmationService().LoadAsync([]);

        Assert.Empty(result.Items);
        Assert.Equal(0, result.AccountsChecked);
        Assert.Empty(result.FailureDetails!);
    }
}
