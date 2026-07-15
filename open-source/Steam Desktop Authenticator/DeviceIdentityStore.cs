using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace Steam_Desktop_Authenticator
{
    internal sealed class DeviceIdentityInfo
    {
        public string DeviceId { get; init; }
        public string PublicKeyDerBase64Url { get; init; }
        public string EncryptionPublicKeyDerBase64Url { get; init; }
        public string EncryptionKeySignatureBase64Url { get; init; }
        public bool HardwareBacked { get; init; }
        public string Protection { get; init; }
    }

    internal static class DeviceIdentityStore
    {
        private const string KeyName = "SDA++ Device Identity v1";
        private const string EncryptionKeyName = "SDA++ Device Encryption v1";
        private static readonly CngProvider[] Providers =
        {
            CngProvider.MicrosoftPlatformCryptoProvider,
            CngProvider.MicrosoftSoftwareKeyStorageProvider
        };

        public static DeviceIdentityInfo GetInfo()
        {
            using CngKey key = OpenOrCreateKey();
            using ECDsaCng signer = new ECDsaCng(key);
            byte[] publicKey = signer.ExportSubjectPublicKeyInfo();
            using CngKey encryptionKey = OpenOrCreateEncryptionKey();
            using RSACng encryptor = new RSACng(encryptionKey);
            byte[] encryptionPublicKey = encryptor.ExportSubjectPublicKeyInfo();
            try
            {
                bool hardwareBacked = key.Provider == CngProvider.MicrosoftPlatformCryptoProvider;
                string deviceId = CreateDeviceId(publicKey);
                string encryptionPublicKeyText = Base64UrlEncode(encryptionPublicKey);
                return new DeviceIdentityInfo
                {
                    DeviceId = deviceId,
                    PublicKeyDerBase64Url = Base64UrlEncode(publicKey),
                    EncryptionPublicKeyDerBase64Url = encryptionPublicKeyText,
                    EncryptionKeySignatureBase64Url = Sign(CanonicalEncryptionKeyBinding(deviceId, encryptionPublicKeyText)),
                    HardwareBacked = hardwareBacked,
                    Protection = hardwareBacked ? "windows-tpm" : "windows-cng-user"
                };
            }
            finally
            {
                CryptographicOperations.ZeroMemory(publicKey);
                CryptographicOperations.ZeroMemory(encryptionPublicKey);
            }
        }

        public static string Sign(byte[] payload)
        {
            if (payload == null) throw new ArgumentNullException(nameof(payload));
            using CngKey key = OpenOrCreateKey();
            using ECDsaCng signer = new ECDsaCng(key);
            return Base64UrlEncode(signer.SignData(
                payload,
                HashAlgorithmName.SHA256,
                DSASignatureFormat.Rfc3279DerSequence));
        }

        public static bool Verify(string publicKeyDerBase64Url, byte[] payload, string signatureBase64Url)
        {
            try
            {
                using ECDsa verifier = ECDsa.Create();
                verifier.ImportSubjectPublicKeyInfo(Base64UrlDecode(publicKeyDerBase64Url), out _);
                return verifier.VerifyData(
                    payload,
                    Base64UrlDecode(signatureBase64Url),
                    HashAlgorithmName.SHA256,
                    DSASignatureFormat.Rfc3279DerSequence);
            }
            catch (Exception ex) when (ex is FormatException || ex is CryptographicException || ex is ArgumentException)
            {
                return false;
            }
        }

        public static bool VerifyEncryptionKeyBinding(
            string deviceId,
            string signingPublicKeyDerBase64Url,
            string encryptionPublicKeyDerBase64Url,
            string signatureBase64Url)
        {
            try
            {
                byte[] signingPublicKey = Base64UrlDecode(signingPublicKeyDerBase64Url);
                if (!string.Equals(CreateDeviceId(signingPublicKey), deviceId, StringComparison.Ordinal)) return false;
                return Verify(
                    signingPublicKeyDerBase64Url,
                    CanonicalEncryptionKeyBinding(deviceId, encryptionPublicKeyDerBase64Url),
                    signatureBase64Url);
            }
            catch (Exception ex) when (ex is FormatException || ex is CryptographicException || ex is ArgumentException)
            {
                return false;
            }
        }

        public static byte[] CanonicalEncryptionKeyBinding(string deviceId, string encryptionPublicKeyDerBase64Url)
        {
            StringBuilder builder = new StringBuilder("sdapp-device-encryption-key/v1\n");
            AddField(builder, "deviceId", deviceId);
            AddField(builder, "publicKey", encryptionPublicKeyDerBase64Url);
            return Encoding.UTF8.GetBytes(builder.ToString());
        }

        public static string EncryptRemoteAction(
            string recipientEncryptionPublicKey,
            byte[] plaintext,
            string vaultId,
            string actionId,
            string direction)
        {
            if (plaintext == null || plaintext.Length > 96 * 1024) throw new ArgumentException("Remote action payload is too large.");
            byte[] aesKey = RandomNumberGenerator.GetBytes(32);
            byte[] nonce = RandomNumberGenerator.GetBytes(12);
            byte[] ciphertext = new byte[plaintext.Length];
            byte[] tag = new byte[16];
            try
            {
                using AesGcm aes = new AesGcm(aesKey, 16);
                aes.Encrypt(nonce, plaintext, ciphertext, tag, RemoteActionEnvelopeCodec.AssociatedData(vaultId, actionId, direction));
                using RSA rsa = RSA.Create();
                rsa.ImportSubjectPublicKeyInfo(Base64UrlDecode(recipientEncryptionPublicKey), out _);
                return RemoteActionEnvelopeCodec.Encode(new RemoteActionEnvelope
                {
                    WrappedKey = rsa.Encrypt(aesKey, RSAEncryptionPadding.OaepSHA256),
                    Nonce = nonce,
                    Ciphertext = ciphertext,
                    Tag = tag
                });
            }
            finally { CryptographicOperations.ZeroMemory(aesKey); }
        }

        public static byte[] DecryptRemoteAction(string envelopeValue, string vaultId, string actionId, string direction)
        {
            RemoteActionEnvelope envelope = RemoteActionEnvelopeCodec.Decode(envelopeValue);
            byte[] aesKey;
            using (CngKey encryptionKey = OpenOrCreateEncryptionKey())
            using (RSACng rsa = new RSACng(encryptionKey))
                aesKey = rsa.Decrypt(envelope.WrappedKey, RSAEncryptionPadding.OaepSHA256);
            try
            {
                byte[] plaintext = new byte[envelope.Ciphertext.Length];
                using AesGcm aes = new AesGcm(aesKey, 16);
                aes.Decrypt(
                    envelope.Nonce,
                    envelope.Ciphertext,
                    envelope.Tag,
                    plaintext,
                    RemoteActionEnvelopeCodec.AssociatedData(vaultId, actionId, direction));
                return plaintext;
            }
            finally { CryptographicOperations.ZeroMemory(aesKey); }
        }

        private static CngKey OpenOrCreateKey()
        {
            foreach (CngProvider provider in Providers)
            {
                try
                {
                    if (CngKey.Exists(KeyName, provider, CngKeyOpenOptions.UserKey))
                        return CngKey.Open(KeyName, provider, CngKeyOpenOptions.UserKey);
                }
                catch (CryptographicException)
                {
                    // Try the next provider before creating a replacement identity.
                }
            }

            foreach (CngProvider provider in Providers)
            {
                try
                {
                    return CngKey.Create(
                        CngAlgorithm.ECDsaP256,
                        KeyName,
                        new CngKeyCreationParameters
                        {
                            Provider = provider,
                            KeyCreationOptions = CngKeyCreationOptions.None,
                            ExportPolicy = CngExportPolicies.None,
                            KeyUsage = CngKeyUsages.Signing
                        });
                }
                catch (CryptographicException)
                {
                    // TPM may not be available; use the current-user CNG provider.
                }
            }

            throw new CryptographicException("Windows could not create the SDA++ device identity key.");
        }

        private static CngKey OpenOrCreateEncryptionKey()
        {
            foreach (CngProvider provider in Providers)
            {
                try
                {
                    if (CngKey.Exists(EncryptionKeyName, provider, CngKeyOpenOptions.UserKey))
                        return CngKey.Open(EncryptionKeyName, provider, CngKeyOpenOptions.UserKey);
                }
                catch (CryptographicException)
                {
                    // Try the next provider before creating a replacement key.
                }
            }

            foreach (CngProvider provider in Providers)
            {
                try
                {
                    CngKeyCreationParameters parameters = new CngKeyCreationParameters
                    {
                        Provider = provider,
                        KeyCreationOptions = CngKeyCreationOptions.None,
                        ExportPolicy = CngExportPolicies.None,
                        KeyUsage = CngKeyUsages.Decryption
                    };
                    parameters.Parameters.Add(new CngProperty("Length", BitConverter.GetBytes(2048), CngPropertyOptions.None));
                    return CngKey.Create(CngAlgorithm.Rsa, EncryptionKeyName, parameters);
                }
                catch (CryptographicException)
                {
                    // TPM may not support this key; use the current-user CNG provider.
                }
            }
            throw new CryptographicException("Windows could not create the SDA++ device encryption key.");
        }

        private static void AddField(StringBuilder builder, string name, string value)
        {
            value ??= string.Empty;
            builder.Append(name).Append('=').Append(Encoding.UTF8.GetByteCount(value)).Append(':').Append(value).Append('\n');
        }

        private static string CreateDeviceId(byte[] publicKey)
        {
            byte[] digest = SHA256.HashData(publicKey);
            try { return Base64UrlEncode(digest.Take(16).ToArray()); }
            finally { CryptographicOperations.ZeroMemory(digest); }
        }

        private static string Base64UrlEncode(byte[] value) =>
            Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');

        private static byte[] Base64UrlDecode(string value)
        {
            string padded = (value ?? string.Empty).Replace('-', '+').Replace('_', '/');
            padded += new string('=', (4 - padded.Length % 4) % 4);
            return Convert.FromBase64String(padded);
        }
    }

    internal static class PairingIdentityProof
    {
        public static byte[] CanonicalPayload(string sessionId, string ephemeralPublicKey, long expiresAt) =>
            Encoding.UTF8.GetBytes($"sdapp-pairing-key/v1\n{sessionId}\n{ephemeralPublicKey}\n{expiresAt}\n");
    }
}
