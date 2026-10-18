using System.Security.Cryptography;
using System.Text;

namespace SDAPlusPlus.Core.Vault;

internal static class LegacyVaultCrypto
{
    private const int Pbkdf2Iterations = 50_000;
    private const int KeySizeBytes = 32;
    private const int SaltSizeBytes = 8;
    private const int IvSizeBytes = 16;

    public static (string CipherText, string Salt, string Iv) Encrypt(string password, string plaintext)
    {
        ArgumentException.ThrowIfNullOrEmpty(password);
        ArgumentException.ThrowIfNullOrEmpty(plaintext);

        byte[] salt = RandomNumberGenerator.GetBytes(SaltSizeBytes);
        byte[] iv = RandomNumberGenerator.GetBytes(IvSizeBytes);
        byte[]? key = null;
        try
        {
            key = Rfc2898DeriveBytes.Pbkdf2(password, salt, Pbkdf2Iterations, HashAlgorithmName.SHA1, KeySizeBytes);
            using Aes aes = Aes.Create();
            aes.KeySize = 256;
            aes.BlockSize = 128;
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.PKCS7;
            aes.Key = key;
            aes.IV = iv;

            using var output = new MemoryStream();
            using (ICryptoTransform encryptor = aes.CreateEncryptor())
            using (var crypto = new CryptoStream(output, encryptor, CryptoStreamMode.Write, leaveOpen: true))
            using (var writer = new StreamWriter(crypto, Encoding.UTF8))
            {
                writer.Write(plaintext);
            }

            return (
                Convert.ToBase64String(output.ToArray()),
                Convert.ToBase64String(salt),
                Convert.ToBase64String(iv));
        }
        finally
        {
            if (key is not null)
            {
                CryptographicOperations.ZeroMemory(key);
            }

            CryptographicOperations.ZeroMemory(salt);
            CryptographicOperations.ZeroMemory(iv);
        }
    }

    public static bool TryDecrypt(
        string password,
        string saltBase64,
        string ivBase64,
        string cipherTextBase64,
        out string plaintext)
    {
        plaintext = string.Empty;
        if (string.IsNullOrEmpty(password) ||
            string.IsNullOrWhiteSpace(saltBase64) ||
            string.IsNullOrWhiteSpace(ivBase64) ||
            string.IsNullOrWhiteSpace(cipherTextBase64))
        {
            return false;
        }

        byte[]? key = null;
        try
        {
            byte[] salt = Convert.FromBase64String(saltBase64);
            byte[] iv = Convert.FromBase64String(ivBase64);
            byte[] cipherText = Convert.FromBase64String(cipherTextBase64);
            key = Rfc2898DeriveBytes.Pbkdf2(password, salt, Pbkdf2Iterations, HashAlgorithmName.SHA1, KeySizeBytes);

            using Aes aes = Aes.Create();
            aes.KeySize = 256;
            aes.BlockSize = 128;
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.PKCS7;
            aes.Key = key;
            aes.IV = iv;

            using var input = new MemoryStream(cipherText, writable: false);
            using var decryptor = aes.CreateDecryptor();
            using var crypto = new CryptoStream(input, decryptor, CryptoStreamMode.Read);
            using var reader = new StreamReader(crypto, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            plaintext = reader.ReadToEnd();
            return !string.IsNullOrWhiteSpace(plaintext);
        }
        catch (Exception exception) when (exception is CryptographicException or FormatException or IOException)
        {
            plaintext = string.Empty;
            return false;
        }
        finally
        {
            if (key is not null)
            {
                CryptographicOperations.ZeroMemory(key);
            }
        }
    }
}
