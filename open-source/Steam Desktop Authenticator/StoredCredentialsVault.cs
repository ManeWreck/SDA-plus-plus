using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace Steam_Desktop_Authenticator
{
    internal sealed class StoredCredentialsVault
    {
        private const string LegacyVaultFileName = "credentials.json";
        private static readonly byte[] LegacyEntropy = Encoding.UTF8.GetBytes("SDA++ by Manewreck Credential Vault");
        private readonly CloudSecretStore secretStore = new CloudSecretStore();

        public sealed class StoredCredential
        {
            public ulong SteamId { get; set; }
            public string AccountName { get; set; }
            public string Username { get; set; }
            public string Password { get; set; }
            public DateTime UpdatedUtc { get; set; }
        }

        public sealed class VaultStatus
        {
            public string Path { get; set; }
            public bool FileExists { get; set; }
            public bool Encrypted { get; set; }
            public bool DecryptionKeyAvailable { get; set; }
            public int EntryCount { get; set; }
        }

        private sealed class VaultDocument
        {
            [JsonProperty("version")]
            public int Version { get; set; } = 2;

            [JsonProperty("encrypted")]
            public bool Encrypted { get; set; }

            [JsonProperty("algorithm")]
            public string Algorithm { get; set; } = "none";

            [JsonProperty("iv")]
            public string Iv { get; set; }

            [JsonProperty("payload")]
            public string Payload { get; set; }

            [JsonProperty("entries")]
            public List<VaultEntry> Entries { get; set; } = new List<VaultEntry>();

            [JsonProperty("updated_utc")]
            public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;
        }

        private sealed class VaultEntry
        {
            [JsonProperty("steam_id")]
            public ulong SteamId { get; set; }

            [JsonProperty("account_name")]
            public string AccountName { get; set; }

            [JsonProperty("username")]
            public string Username { get; set; }

            [JsonProperty("password")]
            public string Password { get; set; }

            [JsonProperty("updated_utc")]
            public DateTime UpdatedUtc { get; set; }
        }

        private sealed class LegacyVaultModel
        {
            [JsonProperty("entries")]
            public List<LegacyVaultEntry> Entries { get; set; } = new List<LegacyVaultEntry>();
        }

        private sealed class LegacyVaultEntry
        {
            [JsonProperty("steam_id")]
            public ulong SteamId { get; set; }

            [JsonProperty("username")]
            public string ProtectedUsername { get; set; }

            [JsonProperty("password")]
            public string ProtectedPassword { get; set; }

            [JsonProperty("updated_utc")]
            public DateTime UpdatedUtc { get; set; }
        }

        public string ResolveVaultPath()
        {
            Manifest manifest = Manifest.GetManifest(true);
            string configured = string.IsNullOrWhiteSpace(manifest.CredentialsStoragePath)
                ? @"maFiles\credentials.secure.json"
                : manifest.CredentialsStoragePath.Trim();
            if (Path.IsPathRooted(configured))
            {
                return configured;
            }

            return Path.GetFullPath(Path.Combine(Manifest.GetExecutableDir(), configured));
        }

        public VaultStatus GetStatus()
        {
            string path = ResolveVaultPath();
            VaultDocument document = TryLoadDocument(path, out List<StoredCredential> entries);
            return new VaultStatus
            {
                Path = path,
                FileExists = File.Exists(path),
                Encrypted = document?.Encrypted ?? Manifest.GetManifest(true).CredentialsRequireEncryption,
                DecryptionKeyAvailable = !string.IsNullOrWhiteSpace(GetRememberedKey(path)),
                EntryCount = entries?.Count ?? 0
            };
        }

        public List<StoredCredential> GetAllCredentials()
        {
            TryLoadDocument(ResolveVaultPath(), out List<StoredCredential> entries);
            return entries ?? new List<StoredCredential>();
        }

        public bool HasCredentials(ulong steamId, string accountName = null)
        {
            return TryGetCredentials(steamId, accountName, out _);
        }

        public bool TryGetCredentials(ulong steamId, string accountName, out StoredCredential credential)
        {
            credential = null;
            List<StoredCredential> entries = GetAllCredentials();

            if (steamId != 0)
            {
                credential = entries.FirstOrDefault(item => item.SteamId == steamId);
            }

            if (credential == null && !string.IsNullOrWhiteSpace(accountName))
            {
                credential = entries.FirstOrDefault(item => string.Equals(item.AccountName, accountName, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(item.Username, accountName, StringComparison.OrdinalIgnoreCase));
            }

            return credential != null
                && !string.IsNullOrWhiteSpace(credential.Username)
                && !string.IsNullOrWhiteSpace(credential.Password);
        }

        public void SaveCredentials(ulong steamId, string accountName, string username, string password)
        {
            if (string.IsNullOrWhiteSpace(accountName))
            {
                accountName = username;
            }

            List<StoredCredential> entries = GetAllCredentials();
            StoredCredential existing = entries.FirstOrDefault(item =>
                (steamId != 0 && item.SteamId == steamId)
                || (!string.IsNullOrWhiteSpace(accountName) && string.Equals(item.AccountName, accountName, StringComparison.OrdinalIgnoreCase))
                || (!string.IsNullOrWhiteSpace(username) && string.Equals(item.Username, username, StringComparison.OrdinalIgnoreCase)));

            if (existing == null)
            {
                existing = new StoredCredential();
                entries.Add(existing);
            }

            existing.SteamId = steamId;
            existing.AccountName = accountName?.Trim() ?? string.Empty;
            existing.Username = username?.Trim() ?? string.Empty;
            existing.Password = password ?? string.Empty;
            existing.UpdatedUtc = DateTime.UtcNow;
            SaveEntries(entries);
        }

        public void ReplaceAllCredentials(IEnumerable<StoredCredential> credentials)
        {
            SaveEntries((credentials ?? Enumerable.Empty<StoredCredential>())
                .Where(item => !string.IsNullOrWhiteSpace(item.AccountName) && !string.IsNullOrWhiteSpace(item.Username))
                .Select(CloneCredential)
                .ToList());
        }

        public void RemoveCredentials(ulong steamId, string accountName = null)
        {
            List<StoredCredential> entries = GetAllCredentials();
            int removed = entries.RemoveAll(item =>
                (steamId != 0 && item.SteamId == steamId)
                || (!string.IsNullOrWhiteSpace(accountName) && string.Equals(item.AccountName, accountName, StringComparison.OrdinalIgnoreCase)));
            if (removed > 0)
            {
                SaveEntries(entries);
            }
        }

        public List<StoredCredential> ParseCredentialPairs(string filePath)
        {
            List<StoredCredential> imported = new List<StoredCredential>();
            foreach (string rawLine in File.ReadAllLines(filePath))
            {
                string line = rawLine?.Trim();
                if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#", StringComparison.Ordinal))
                {
                    continue;
                }

                int separator = line.IndexOf(':');
                if (separator <= 0)
                {
                    continue;
                }

                string login = line.Substring(0, separator).Trim();
                string password = line.Substring(separator + 1);
                if (string.IsNullOrWhiteSpace(login) || string.IsNullOrWhiteSpace(password))
                {
                    continue;
                }

                imported.Add(new StoredCredential
                {
                    SteamId = 0,
                    AccountName = login,
                    Username = login,
                    Password = password,
                    UpdatedUtc = DateTime.UtcNow
                });
            }

            return imported;
        }

        public string GenerateEncryptionKey()
        {
            byte[] key = new byte[32];
            RandomNumberGenerator.Fill(key);
            try
            {
                return Convert.ToBase64String(key);
            }
            finally
            {
                Array.Clear(key, 0, key.Length);
            }
        }

        public void StoreRememberedKey(string path, string key)
        {
            secretStore.Save(GetKeySecretName(path), key ?? string.Empty);
        }

        public bool HasRememberedKey(string path)
        {
            return !string.IsNullOrWhiteSpace(GetRememberedKey(path));
        }

        public string TryGetRememberedKey(string path)
        {
            return GetRememberedKey(path);
        }

        public void ClearRememberedKey(string path)
        {
            secretStore.Save(GetKeySecretName(path), string.Empty);
        }

        public void ConfigureStorage(string path, bool encrypted, string encryptionKey, bool rememberKey)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new InvalidOperationException("Credentials storage path is required.");
            }

            string resolvedPath = ResolveConfiguredPath(path);
            List<StoredCredential> entries = GetAllCredentials();
            Manifest manifest = Manifest.GetManifest(true);
            manifest.CredentialsStoragePath = path;
            manifest.CredentialsRequireEncryption = encrypted;
            manifest.Save();

            if (rememberKey && !string.IsNullOrWhiteSpace(encryptionKey))
            {
                StoreRememberedKey(resolvedPath, encryptionKey);
            }
            else if (!encrypted)
            {
                ClearRememberedKey(resolvedPath);
            }

            SaveEntries(entries, resolvedPath, encrypted, encryptionKey);
        }

        private void SaveEntries(List<StoredCredential> entries)
        {
            Manifest manifest = Manifest.GetManifest(true);
            SaveEntries(entries, ResolveVaultPath(), manifest.CredentialsRequireEncryption, GetRememberedKey(ResolveVaultPath()));
        }

        private void SaveEntries(List<StoredCredential> entries, string path, bool encrypted, string encryptionKey)
        {
            string directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            VaultDocument document = new VaultDocument
            {
                UpdatedUtc = DateTime.UtcNow,
                Encrypted = encrypted,
                Entries = entries
                    .OrderBy(item => item.AccountName, StringComparer.OrdinalIgnoreCase)
                    .Select(item => new VaultEntry
                    {
                        SteamId = item.SteamId,
                        AccountName = item.AccountName?.Trim() ?? string.Empty,
                        Username = item.Username?.Trim() ?? string.Empty,
                        Password = item.Password ?? string.Empty,
                        UpdatedUtc = item.UpdatedUtc == default ? DateTime.UtcNow : item.UpdatedUtc
                    })
                    .ToList()
            };

            if (encrypted)
            {
                if (string.IsNullOrWhiteSpace(encryptionKey))
                {
                    throw new InvalidOperationException("A decryption key is required for the encrypted credentials vault.");
                }

                string plainPayload = JsonConvert.SerializeObject(document.Entries, Formatting.None);
                EncryptDocument(document, plainPayload, encryptionKey);
                document.Entries = null;
            }
            else
            {
                document.Algorithm = "none";
                document.Iv = null;
                document.Payload = null;
            }

            string json = JsonConvert.SerializeObject(document, Formatting.Indented);
            File.WriteAllText(path, json);
        }

        private VaultDocument TryLoadDocument(string path, out List<StoredCredential> entries)
        {
            entries = new List<StoredCredential>();
            if (!File.Exists(path))
            {
                TryMigrateLegacyVault(path, out entries);
                return new VaultDocument
                {
                    Encrypted = Manifest.GetManifest(true).CredentialsRequireEncryption,
                    Entries = entries.Select(item => new VaultEntry
                    {
                        SteamId = item.SteamId,
                        AccountName = item.AccountName,
                        Username = item.Username,
                        Password = item.Password,
                        UpdatedUtc = item.UpdatedUtc
                    }).ToList()
                };
            }

            string json = File.ReadAllText(path);
            VaultDocument document = JsonConvert.DeserializeObject<VaultDocument>(json);
            if (document == null)
            {
                return null;
            }

            if (document.Encrypted)
            {
                string rememberedKey = GetRememberedKey(path);
                if (string.IsNullOrWhiteSpace(rememberedKey))
                {
                    return document;
                }

                string payload = DecryptPayload(document, rememberedKey);
                if (payload == null)
                {
                    return document;
                }

                List<VaultEntry> decryptedEntries = JsonConvert.DeserializeObject<List<VaultEntry>>(payload) ?? new List<VaultEntry>();
                entries = decryptedEntries.Select(MapEntry).ToList();
                return document;
            }

            entries = (document.Entries ?? new List<VaultEntry>()).Select(MapEntry).ToList();
            return document;
        }

        private void TryMigrateLegacyVault(string targetPath, out List<StoredCredential> entries)
        {
            entries = new List<StoredCredential>();
            string legacyPath = Path.Combine(Manifest.GetExecutableDir(), "maFiles", LegacyVaultFileName);
            if (!File.Exists(legacyPath) || string.Equals(legacyPath, targetPath, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            try
            {
                LegacyVaultModel legacy = JsonConvert.DeserializeObject<LegacyVaultModel>(File.ReadAllText(legacyPath));
                foreach (LegacyVaultEntry item in legacy?.Entries ?? new List<LegacyVaultEntry>())
                {
                    string username = TryUnprotectLegacy(item.ProtectedUsername);
                    string password = TryUnprotectLegacy(item.ProtectedPassword);
                    if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
                    {
                        continue;
                    }

                    entries.Add(new StoredCredential
                    {
                        SteamId = item.SteamId,
                        AccountName = username,
                        Username = username,
                        Password = password,
                        UpdatedUtc = item.UpdatedUtc
                    });
                }

                if (entries.Count > 0)
                {
                    SaveEntries(entries, targetPath, Manifest.GetManifest(true).CredentialsRequireEncryption, GetRememberedKey(targetPath));
                }
            }
            catch
            {
            }
        }

        private static StoredCredential MapEntry(VaultEntry entry)
        {
            return new StoredCredential
            {
                SteamId = entry?.SteamId ?? 0,
                AccountName = entry?.AccountName ?? string.Empty,
                Username = entry?.Username ?? string.Empty,
                Password = entry?.Password ?? string.Empty,
                UpdatedUtc = entry?.UpdatedUtc ?? DateTime.UtcNow
            };
        }

        private static StoredCredential CloneCredential(StoredCredential item)
        {
            return new StoredCredential
            {
                SteamId = item.SteamId,
                AccountName = item.AccountName,
                Username = item.Username,
                Password = item.Password,
                UpdatedUtc = item.UpdatedUtc
            };
        }

        private static string ResolveConfiguredPath(string configuredPath)
        {
            return Path.IsPathRooted(configuredPath)
                ? configuredPath
                : Path.GetFullPath(Path.Combine(Manifest.GetExecutableDir(), configuredPath));
        }

        private string GetRememberedKey(string path)
        {
            return secretStore.Load(GetKeySecretName(path));
        }

        private static string GetKeySecretName(string path)
        {
            using SHA256 sha = SHA256.Create();
            byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(path.ToLowerInvariant()));
            return "credentials-file-key-" + Convert.ToBase64String(hash).Replace("/", "_").Replace("+", "-");
        }

        private static void EncryptDocument(VaultDocument document, string plainPayload, string encryptionKey)
        {
            byte[] key = NormalizeKey(encryptionKey);
            byte[] iv = new byte[16];
            RandomNumberGenerator.Fill(iv);
            byte[] plainBytes = Encoding.UTF8.GetBytes(plainPayload ?? string.Empty);

            try
            {
                using Aes aes = Aes.Create();
                aes.KeySize = 256;
                aes.Mode = CipherMode.CBC;
                aes.Padding = PaddingMode.PKCS7;
                aes.Key = key;
                aes.IV = iv;

                using ICryptoTransform encryptor = aes.CreateEncryptor();
                byte[] cipherBytes = encryptor.TransformFinalBlock(plainBytes, 0, plainBytes.Length);
                document.Algorithm = "aes-256-cbc";
                document.Iv = Convert.ToBase64String(iv);
                document.Payload = Convert.ToBase64String(cipherBytes);
            }
            finally
            {
                Array.Clear(key, 0, key.Length);
                Array.Clear(iv, 0, iv.Length);
                Array.Clear(plainBytes, 0, plainBytes.Length);
            }
        }

        private static string DecryptPayload(VaultDocument document, string encryptionKey)
        {
            if (document == null || string.IsNullOrWhiteSpace(document.Payload) || string.IsNullOrWhiteSpace(document.Iv))
            {
                return null;
            }

            byte[] key = NormalizeKey(encryptionKey);
            byte[] iv = Convert.FromBase64String(document.Iv);
            byte[] cipherBytes = Convert.FromBase64String(document.Payload);
            try
            {
                using Aes aes = Aes.Create();
                aes.KeySize = 256;
                aes.Mode = CipherMode.CBC;
                aes.Padding = PaddingMode.PKCS7;
                aes.Key = key;
                aes.IV = iv;
                using ICryptoTransform decryptor = aes.CreateDecryptor();
                byte[] plainBytes = decryptor.TransformFinalBlock(cipherBytes, 0, cipherBytes.Length);
                try
                {
                    return Encoding.UTF8.GetString(plainBytes);
                }
                finally
                {
                    Array.Clear(plainBytes, 0, plainBytes.Length);
                }
            }
            catch
            {
                return null;
            }
            finally
            {
                Array.Clear(key, 0, key.Length);
                Array.Clear(iv, 0, iv.Length);
                Array.Clear(cipherBytes, 0, cipherBytes.Length);
            }
        }

        private static byte[] NormalizeKey(string encryptionKey)
        {
            if (string.IsNullOrWhiteSpace(encryptionKey))
            {
                throw new InvalidOperationException("Credentials encryption key is missing.");
            }

            try
            {
                byte[] raw = Convert.FromBase64String(encryptionKey.Trim());
                if (raw.Length == 32)
                {
                    return raw;
                }

                using SHA256 sha = SHA256.Create();
                byte[] hash = sha.ComputeHash(raw);
                Array.Clear(raw, 0, raw.Length);
                return hash;
            }
            catch
            {
                using SHA256 sha = SHA256.Create();
                return sha.ComputeHash(Encoding.UTF8.GetBytes(encryptionKey.Trim()));
            }
        }

        private static string TryUnprotectLegacy(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            try
            {
                byte[] protectedBytes = Convert.FromBase64String(value);
                byte[] bytes = ProtectedData.Unprotect(protectedBytes, LegacyEntropy, DataProtectionScope.CurrentUser);
                try
                {
                    return Encoding.UTF8.GetString(bytes);
                }
                finally
                {
                    Array.Clear(bytes, 0, bytes.Length);
                }
            }
            catch
            {
                return null;
            }
        }
    }
}
