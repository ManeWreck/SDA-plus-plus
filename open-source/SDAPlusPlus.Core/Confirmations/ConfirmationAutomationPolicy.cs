namespace SDAPlusPlus.Core.Confirmations;

public static class ConfirmationAutomationPolicy
{
    public static bool IsVerifiedGiftTrade(ConfirmationDetailsResult details) =>
        details.Success &&
        details.Items.Count > 0 &&
        details.Items.All(item => item.Side == ConfirmationTradeSide.Receive);
}
