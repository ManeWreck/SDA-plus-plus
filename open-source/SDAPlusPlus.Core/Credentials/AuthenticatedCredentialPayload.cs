using System.Security.Cryptography;
using System.Text;

namespace SDAPlusPlus.Core.Credentials;

public static class AuthenticatedCredentialPayload
{
    public const string Algorithm = "aes-256-gcm";
    private static readonly byte[] Aad = Encoding.UTF8.GetBytes("SDA++/credentials/v3");
    private const int MaxPayload = 4 * 1024 * 1024;

    public static (string Iv, string Payload) Encrypt(byte[] plain, string base64Key)
    {
        if (plain.Length > MaxPayload) throw new CryptographicException("Credentials payload is too large.");
        byte[] key = ReadKey(base64Key);
        byte[] nonce = RandomNumberGenerator.GetBytes(12);
        byte[] packed = new byte[plain.Length + 16];
        try
        {
            using var aes = new AesGcm(key, 16);
            aes.Encrypt(nonce, plain, packed.AsSpan(0, plain.Length), packed.AsSpan(plain.Length), Aad);
            return (Convert.ToBase64String(nonce), Convert.ToBase64String(packed));
        }
        finally { CryptographicOperations.ZeroMemory(key); }
    }

    public static byte[] Decrypt(int version, string algorithm, string iv, string payload, string base64Key)
    {
        if (version != 3 || algorithm != Algorithm || iv.Length != 16 || payload.Length > ((MaxPayload + 18) / 3) * 4)
            throw new CryptographicException("Unsupported credentials envelope.");
        byte[] nonce = Convert.FromBase64String(iv);
        byte[] packed = Convert.FromBase64String(payload);
        if (nonce.Length != 12 || packed.Length < 16 || packed.Length > MaxPayload + 16)
            throw new CryptographicException("Invalid credentials envelope.");
        byte[] key = ReadKey(base64Key);
        byte[] plain = new byte[packed.Length - 16];
        try
        {
            using var aes = new AesGcm(key, 16);
            aes.Decrypt(nonce, packed.AsSpan(0, plain.Length), packed.AsSpan(plain.Length), plain, Aad);
            return plain;
        }
        catch { CryptographicOperations.ZeroMemory(plain); throw; }
        finally { CryptographicOperations.ZeroMemory(key); }
    }

    private static byte[] ReadKey(string value)
    {
        byte[] key = Convert.FromBase64String(value.Trim());
        if (key.Length == 32) return key;
        CryptographicOperations.ZeroMemory(key);
        throw new CryptographicException("A 32-byte base64 credentials key is required.");
    }
}
