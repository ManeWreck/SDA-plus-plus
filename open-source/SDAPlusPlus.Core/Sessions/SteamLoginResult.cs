using SteamAuth;

namespace SDAPlusPlus.Core.Sessions;

public enum SteamLoginState
{
    Succeeded,
    InvalidInput,
    EmailCodeRequired,
    Cancelled,
    ConnectionFailed,
    AuthenticationFailed
}

public sealed record SteamLoginResult(SteamLoginState State, SessionData? Session = null, string? Detail = null)
{
    public bool IsSuccess => State == SteamLoginState.Succeeded && Session is not null;
}
