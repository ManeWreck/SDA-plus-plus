using SDAPlusPlus.Core.Sessions;
using SteamAuth;

namespace SDAPlusPlus.Core.Tests;

public sealed class SteamSessionTerminationServiceTests
{
    private readonly SteamSessionTerminationService _service = new();

    [Fact]
    public async Task RejectsAccountWithoutRefreshSessionBeforeNetworkRequest()
    {
        var account = new SteamGuardAccount { Session = new SessionData() };

        SteamSessionTerminationResult result = await _service.TerminateAllAsync(account);

        Assert.Equal(SteamSessionTerminationState.MissingSession, result.State);
    }

    [Fact]
    public async Task RejectsExpiredRefreshTokenBeforeNetworkRequest()
    {
        var account = new SteamGuardAccount
        {
            Session = new SessionData
            {
                SteamID = 76561198000000000,
                RefreshToken = "header.eyJleHAiOjB9.signature"
            }
        };

        SteamSessionTerminationResult result = await _service.TerminateAllAsync(account);

        Assert.Equal(SteamSessionTerminationState.ExpiredSession, result.State);
    }
}
