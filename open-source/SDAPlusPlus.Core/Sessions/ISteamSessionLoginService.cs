using SteamAuth;

namespace SDAPlusPlus.Core.Sessions;

public interface ISteamSessionLoginService
{
    Task<SteamLoginResult> LoginAsync(
        SteamGuardAccount account,
        string username,
        string password,
        Func<string, bool, CancellationToken, Task<string?>>? requestEmailCode = null,
        CancellationToken cancellationToken = default);
}
