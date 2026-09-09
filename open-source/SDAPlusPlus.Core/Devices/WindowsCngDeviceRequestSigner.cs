using System.Security.Cryptography;
using System.Runtime.Versioning;

namespace SDAPlusPlus.Core.Devices;

[SupportedOSPlatform("windows")]
public sealed class WindowsCngDeviceRequestSigner : IDeviceRequestSigner, IDisposable
{
    private const string KeyName = "SDA++ Device Identity v1";
    private const string EncryptionKeyName = "SDA++ Device Encryption v1";
    private readonly CngKey _key;
    private readonly CngKey _encryptionKey;

    public WindowsCngDeviceRequestSigner(bool createIfMissing = false)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("The existing SDA++ identity uses Windows CNG.");

        _key = OpenExistingKey() ?? (createIfMissing ? CreateSigningKey() : null)
            ?? throw new CryptographicException("The SDA++ device identity key was not found for this Windows user.");
        _encryptionKey = OpenExistingKey(EncryptionKeyName) ?? (createIfMissing ? CreateEncryptionKey() : null)
            ?? throw new CryptographicException("The SDA++ device encryption key was not found for this Windows user.");
        using ECDsaCng signer = new(_key);
        byte[] publicKey = signer.ExportSubjectPublicKeyInfo();
        using RSACng encryptor = new(_encryptionKey);
        byte[] encryptionPublicKey = encryptor.ExportSubjectPublicKeyInfo();
        byte[] digest = SHA256.HashData(publicKey);
        try
        {
            DeviceId = Base64Url(digest[..16]);
            PublicKey = Base64Url(publicKey);
            EncryptionPublicKey = Base64Url(encryptionPublicKey);
            EncryptionKeySignature = Sign(CreateEncryptionKeyBinding(DeviceId, EncryptionPublicKey));
            Protection = _key.Provider == CngProvider.MicrosoftPlatformCryptoProvider ? "windows-tpm" : "windows-cng-user";
        }
        finally
        {
            CryptographicOperations.ZeroMemory(publicKey);
            CryptographicOperations.ZeroMemory(encryptionPublicKey);
            CryptographicOperations.ZeroMemory(digest);
        }
    }

    public string DeviceId { get; }
    public string PublicKey { get; }
    public string EncryptionPublicKey { get; }
    public string EncryptionKeySignature { get; }
    public string Protection { get; }

    public string Sign(byte[] payload)
    {
        using ECDsaCng signer = new(_key);
        return Base64Url(signer.SignData(payload, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence));
    }

    public void Dispose()
    {
        _encryptionKey.Dispose();
        _key.Dispose();
    }

    private static CngKey? OpenExistingKey(string keyName = KeyName)
    {
        foreach (CngProvider provider in new[]
                 {
                     CngProvider.MicrosoftPlatformCryptoProvider,
                     CngProvider.MicrosoftSoftwareKeyStorageProvider
                 })
        {
            try
            {
                if (CngKey.Exists(keyName, provider, CngKeyOpenOptions.UserKey))
                    return CngKey.Open(keyName, provider, CngKeyOpenOptions.UserKey);
            }
            catch (CryptographicException)
            {
                // The same identity may exist in the next provider.
            }
        }

        return null;
    }

    private static CngKey CreateSigningKey()
    {
        foreach (CngProvider provider in new[] { CngProvider.MicrosoftPlatformCryptoProvider, CngProvider.MicrosoftSoftwareKeyStorageProvider })
        {
            try
            {
                return CngKey.Create(CngAlgorithm.ECDsaP256, KeyName, new CngKeyCreationParameters
                {
                    Provider = provider,
                    KeyCreationOptions = CngKeyCreationOptions.None,
                    ExportPolicy = CngExportPolicies.None
                });
            }
            catch (CryptographicException) when (provider == CngProvider.MicrosoftPlatformCryptoProvider)
            {
            }
        }
        throw new CryptographicException("Windows could not create the SDA++ signing identity.");
    }

    private static CngKey CreateEncryptionKey()
    {
        foreach (CngProvider provider in new[] { CngProvider.MicrosoftPlatformCryptoProvider, CngProvider.MicrosoftSoftwareKeyStorageProvider })
        {
            try
            {
                return CngKey.Create(CngAlgorithm.Rsa, EncryptionKeyName, new CngKeyCreationParameters
                {
                    Provider = provider,
                    KeyCreationOptions = CngKeyCreationOptions.None,
                    ExportPolicy = CngExportPolicies.None,
                    Parameters = { new CngProperty("Length", BitConverter.GetBytes(2048), CngPropertyOptions.None) }
                });
            }
            catch (CryptographicException) when (provider == CngProvider.MicrosoftPlatformCryptoProvider)
            {
            }
        }
        throw new CryptographicException("Windows could not create the SDA++ encryption identity.");
    }

    private static string Base64Url(byte[] value) =>
        Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] CreateEncryptionKeyBinding(string deviceId, string publicKey)
    {
        System.Text.StringBuilder builder = new("sdapp-device-encryption-key/v1\n");
        AddField(builder, "deviceId", deviceId);
        AddField(builder, "publicKey", publicKey);
        return System.Text.Encoding.UTF8.GetBytes(builder.ToString());
    }

    private static void AddField(System.Text.StringBuilder builder, string name, string value) =>
        builder.Append(name).Append('=').Append(System.Text.Encoding.UTF8.GetByteCount(value)).Append(':').Append(value).Append('\n');
}
