using SDAPlusPlus.Core.Credentials;
using System.Security.Cryptography;
using System.Text;

namespace SDAPlusPlus.Core.Tests;

public sealed class AuthenticatedCredentialPayloadTests
{
    private static readonly string Key = Convert.ToBase64String(new byte[32]);
    private const string Iv = "AAAAAAAAAAAAAAAA";
    private const string Payload = "lfqDNbw6Ustnux9+d98CZumx";

    [Fact]
    public void SharedJavaDotNetVector() => Assert.Equal("[]", Encoding.UTF8.GetString(
        AuthenticatedCredentialPayload.Decrypt(3, "aes-256-gcm", Iv, Payload, Key)));

    [Fact]
    public void EncryptUsesFreshNoncesAndRoundTrips()
    {
        var first = AuthenticatedCredentialPayload.Encrypt("test"u8.ToArray(), Key);
        var second = AuthenticatedCredentialPayload.Encrypt("test"u8.ToArray(), Key);
        Assert.NotEqual(first.Iv, second.Iv);
        Assert.Equal("test", Encoding.UTF8.GetString(AuthenticatedCredentialPayload.Decrypt(3, "aes-256-gcm", first.Iv, first.Payload, Key)));
    }

    [Fact]
    public void EveryCiphertextAndTagByteIsAuthenticated()
    {
        for (int i = 0; i < 18; i++)
        {
            byte[] altered = Convert.FromBase64String(Payload);
            altered[i] ^= 1;
            Assert.ThrowsAny<CryptographicException>(() => AuthenticatedCredentialPayload.Decrypt(3, "aes-256-gcm", Iv, Convert.ToBase64String(altered), Key));
        }
    }

    [Fact]
    public void WrongKeyVersionAndAlgorithmFailClosed()
    {
        string other = Convert.ToBase64String(Enumerable.Repeat((byte)1, 32).ToArray());
        Assert.ThrowsAny<CryptographicException>(() => AuthenticatedCredentialPayload.Decrypt(3, "aes-256-gcm", Iv, Payload, other));
        Assert.ThrowsAny<CryptographicException>(() => AuthenticatedCredentialPayload.Decrypt(2, "aes-256-gcm", Iv, Payload, Key));
        Assert.ThrowsAny<CryptographicException>(() => AuthenticatedCredentialPayload.Decrypt(3, "aes-256-cbc", Iv, Payload, Key));
        Assert.ThrowsAny<CryptographicException>(() => AuthenticatedCredentialPayload.Decrypt(3, "aes-256-gcm", Iv, "", Key));
    }
}
