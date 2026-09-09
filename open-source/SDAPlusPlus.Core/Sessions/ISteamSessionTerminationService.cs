using SteamAuth;

namespace SDAPlusPlus.Core.Sessions;

public interface ISteamSessionTerminationService
{
    Task<SteamSessionListResult> GetActiveSessionsAsync(
        SteamGuardAccount account,
        CancellationToken cancellationToken = default);

    Task<SteamSessionTerminationResult> TerminateAllAsync(
        SteamGuardAccount account,
        CancellationToken cancellationToken = default);
}
