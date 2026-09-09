namespace SDAPlusPlus.Core.Confirmations;

public sealed record ConfirmationLoadResult(
    IReadOnlyList<AccountConfirmation> Items,
    int AccountsChecked,
    int ExpiredAccounts,
    int FailedAccounts,
    int RefreshedAccounts = 0,
    IReadOnlyList<string>? FailureDetails = null,
    int NetworkChecks = 0,
    int DeferredAccounts = 0);

public enum ConfirmationDecision
{
    Accept,
    Reject
}

public sealed record ConfirmationActionResult(bool Success);

public enum ConfirmationTradeSide
{
    Give,
    Receive
}

public sealed record ConfirmationTradeItem(string Name, string? ImageUrl, ConfirmationTradeSide Side);

public sealed record ConfirmationDetailsResult(bool Success, IReadOnlyList<ConfirmationTradeItem> Items, string? Error = null);
