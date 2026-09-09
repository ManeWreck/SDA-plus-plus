using SDAPlusPlus.Core.Confirmations;

namespace SDAPlusPlus.Core.Tests;

public sealed class ConfirmationAutomationPolicyTests
{
    [Fact]
    public void IncomingItemsWithoutOutgoingItemsAreVerifiedAsGift()
    {
        var details = new ConfirmationDetailsResult(true,
        [
            new ConfirmationTradeItem("Gift", null, ConfirmationTradeSide.Receive)
        ]);

        Assert.True(ConfirmationAutomationPolicy.IsVerifiedGiftTrade(details));
    }

    [Fact]
    public void TradeWithAnyOutgoingItemIsNotVerifiedAsGift()
    {
        var details = new ConfirmationDetailsResult(true,
        [
            new ConfirmationTradeItem("Outgoing", null, ConfirmationTradeSide.Give),
            new ConfirmationTradeItem("Incoming", null, ConfirmationTradeSide.Receive)
        ]);

        Assert.False(ConfirmationAutomationPolicy.IsVerifiedGiftTrade(details));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MissingOrFailedDetailsAreNotVerifiedAsGift(bool success)
    {
        var details = new ConfirmationDetailsResult(success, []);

        Assert.False(ConfirmationAutomationPolicy.IsVerifiedGiftTrade(details));
    }
}
