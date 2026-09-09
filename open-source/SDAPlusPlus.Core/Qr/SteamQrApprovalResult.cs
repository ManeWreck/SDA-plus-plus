namespace SDAPlusPlus.Core.Qr;

public enum SteamQrApprovalFailure
{
    None,
    InvalidQr,
    MissingAccount,
    MissingSession,
    ExpiredSession,
    MissingSecret,
    SteamRejected
}

public sealed record SteamQrApprovalResult(
    bool Success,
    SteamQrApprovalFailure Failure = SteamQrApprovalFailure.None,
    string? Detail = null);
