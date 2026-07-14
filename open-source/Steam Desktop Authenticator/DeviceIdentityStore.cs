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
        public bool HardwareBacked { get; init; }
        public string Protection { get; init; }
    }

    internal static class DeviceIdentityStore
    {
        private const string KeyName = "SDA++ Device Identity v1";
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
            try
            {
                bool hardwareBacked = key.Provider == CngProvider.MicrosoftPlatformCryptoProvider;
                return new DeviceIdentityInfo
                {
                    DeviceId = CreateDeviceId(publicKey),
                    PublicKeyDerBase64Url = Base64UrlEncode(publicKey),
                    HardwareBacked = hardwareBacked,
                    Protection = hardwareBacked ? "windows-tpm" : "windows-cng-user"
                };
            }
            finally
            {
                CryptographicOperations.ZeroMemory(publicKey);
            }
        }

        public static string Sign(byte[] payload)
        {
            if (payload == null) throw new ArgumentNullException(nameof(payload));
            using CngKey key = OpenOrCreateKey();
            using ECDsaCng signer = new ECDsaCng(key);
            return Base64UrlEncode(signer.SignData(payload, HashAlgorithmName.SHA256));
        }

        public static bool Verify(string publicKeyDerBase64Url, byte[] payload, string signatureBase64Url)
        {
            try
            {
                using ECDsa verifier = ECDsa.Create();
                verifier.ImportSubjectPublicKeyInfo(Base64UrlDecode(publicKeyDerBase64Url), out _);
                return verifier.VerifyData(payload, Base64UrlDecode(signatureBase64Url), HashAlgorithmName.SHA256);
            }
            catch (Exception ex) when (ex is FormatException || ex is CryptographicException || ex is ArgumentException)
            {
                return false;
            }
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
