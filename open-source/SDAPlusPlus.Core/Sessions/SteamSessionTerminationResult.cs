namespace SDAPlusPlus.Core.Sessions;

public enum SteamSessionTerminationState
{
    Succeeded,
    MissingSession,
    ExpiredSession,
    AuthenticationFailed,
    SteamRejected,
    Cancelled
}

public sealed record SteamSessionTerminationResult(SteamSessionTerminationState State, string? Detail = null)
{
    public bool IsSuccess => State == SteamSessionTerminationState.Succeeded;
}
