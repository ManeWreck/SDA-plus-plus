using SDAPlusPlus.Core.Qr;

namespace SDAPlusPlus.Core.Tests;

public sealed class SteamQrParserTests
{
    [Theory]
    [InlineData("https://s.team/q/1/123456789", 1, 123456789UL)]
    [InlineData("https://S.TEAM/q/2/987654321?foo=bar", 2, 987654321UL)]
    public void ValidSteamQrIsParsed(string text, int version, ulong clientId)
    {
        Assert.True(SteamQrParser.TryParse(text, out SteamQrPayload? payload));
        Assert.Equal(version, payload!.Version);
        Assert.Equal(clientId, payload.ClientId);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("https://example.com/q/1/123")]
    [InlineData("http://s.team/q/1/123")]
    [InlineData("https://s.team/q/0/123")]
    [InlineData("https://s.team/q/1/0")]
    [InlineData("https://s.team/q/1/123/extra")]
    [InlineData("steam://openid/login?nonce=test")]
    public void UnsupportedOrMalformedPayloadIsRejected(string? text)
    {
        Assert.False(SteamQrParser.TryParse(text, out _));
    }
}
