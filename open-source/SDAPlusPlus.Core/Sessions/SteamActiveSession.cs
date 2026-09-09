namespace SDAPlusPlus.Core.Sessions;

public sealed record SteamActiveSession(
    ulong TokenId,
    string DeviceName,
    string Platform,
    string IpAddress,
    string Location,
    DateTimeOffset? FirstSeen,
    DateTimeOffset? LastSeen,
    bool IsCurrent,
    bool IsLoggedIn);

public enum SteamSessionListState
{
    Succeeded,
    MissingSession,
    ExpiredSession,
    AuthenticationFailed,
    SteamRejected,
    Cancelled
}

public sealed record SteamSessionListResult(
    SteamSessionListState State,
    IReadOnlyList<SteamActiveSession>? Sessions = null,
    string? Detail = null)
{
    public bool IsSuccess => State == SteamSessionListState.Succeeded;
}
