using System.Security.Cryptography;
using System.Runtime.Versioning;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SDAPlusPlus.Core.Vault;

namespace SDAPlusPlus.Core.Credentials;

public sealed class LegacyStoredCredentialVault(string maFilesDirectory) : IStoredCredentialVault
{
    private static readonly byte[] SecretEntropy = Encoding.UTF8.GetBytes("SDA++ Cloud Secret Store");
    private static readonly byte[] LegacyCredentialEntropy = Encoding.UTF8.GetBytes("SDA++ by Manewreck Credential Vault");
    private readonly string _vaultPath = Path.Combine(Path.GetFullPath(maFilesDirectory), "credentials.secure.json");
    private readonly string _secretPath = Path.Combine(Path.GetFullPath(maFilesDirectory), "cloud.secret.json");
    private string _activeKey = string.Empty;

    public CredentialVaultLoadResult Load()
    {
        if (!File.Exists(_vaultPath))
        {
            return new CredentialVaultLoadResult(CredentialVaultState.Missing, [], _vaultPath, true);
        }

        try
        {
            VaultDocument? document = JsonConvert.DeserializeObject<VaultDocument>(File.ReadAllText(_vaultPath));
            if (document is null)
            {
                return Failed(CredentialVaultState.Invalid, "The credentials document is empty or invalid.");
            }
            if (document.Version >= 3 && (!document.Encrypted || document.Version != 3 || document.Algorithm != AuthenticatedCredentialPayload.Algorithm))
                return Failed(CredentialVaultState.Invalid, "Invalid authenticated credentials envelope.");

            if (!document.Encrypted)
            {
                return new CredentialVaultLoadResult(
                    CredentialVaultState.Ready,
                    MapEntries(document.Entries),
                    _vaultPath,
                    false);
            }

            IReadOnlyList<string> keys = LoadRememberedKeys();
            if (keys.Count == 0)
            {
                return Failed(CredentialVaultState.Locked, "The encrypted credentials key is unavailable for this Windows user.");
            }

            string? payload = null;
            foreach (string key in keys)
            {
                payload = DecryptPayload(document, key);
                if (payload is not null)
                {
                    _activeKey = key;
                    break;
                }
            }
            if (payload is null)
            {
                return Failed(CredentialVaultState.Locked, "The encrypted credentials key could not unlock this file.");
            }

            try
            {
                List<VaultEntry>? entries = JsonConvert.DeserializeObject<List<VaultEntry>>(payload);
                return new CredentialVaultLoadResult(
                    CredentialVaultState.Ready,
                    MapEntries(entries),
                    _vaultPath,
                    true);
            }
            finally
            {
                payload = string.Empty;
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or FormatException or CryptographicException)
        {
            return Failed(CredentialVaultState.Failed, exception.Message);
        }
    }

    public bool TryGet(VaultAccount account, out StoredCredential? credential)
    {
        ArgumentNullException.ThrowIfNull(account);
        CredentialVaultLoadResult load = Load();
        ulong.TryParse(account.SteamId, out ulong steamId);
        credential = load.Credentials.FirstOrDefault(item =>
            (steamId > 0 && item.SteamId == steamId) ||
            IdentityEquals(item.AccountName, account.Name) ||
            IdentityEquals(item.Username, account.Name));
        return load.IsReady && credential is not null &&
            !string.IsNullOrWhiteSpace(credential.Username) && !string.IsNullOrEmpty(credential.Password);
    }

    public CredentialVaultWriteResult CreateEncrypted(string encryptionKey)
    {
        if (File.Exists(_vaultPath))
        {
            return new CredentialVaultWriteResult(CredentialVaultState.Invalid, "The credentials vault already exists.");
        }

        if (!OperatingSystem.IsWindows())
        {
            return new CredentialVaultWriteResult(CredentialVaultState.Failed, "Secure key storage currently requires Windows DPAPI.");
        }

        byte[]? decodedKey = null;
        try
        {
            decodedKey = Convert.FromBase64String(encryptionKey.Trim());
            if (decodedKey.Length != 32)
            {
                return new CredentialVaultWriteResult(CredentialVaultState.Invalid, "The encryption key must contain 32 bytes.");
            }

            Directory.CreateDirectory(Path.GetDirectoryName(_vaultPath)!);
            RememberKey(encryptionKey.Trim());
            _activeKey = encryptionKey.Trim();
            return SaveEntries([], encrypted: true);
        }
        catch (Exception exception) when (exception is FormatException or IOException or UnauthorizedAccessException or CryptographicException)
        {
            return new CredentialVaultWriteResult(CredentialVaultState.Failed, exception.Message);
        }
        finally
        {
            if (decodedKey is not null) CryptographicOperations.ZeroMemory(decodedKey);
        }
    }

    public CredentialVaultWriteResult UpgradeEncryption()
    {
        string temporary = _vaultPath + ".migration-" + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            CredentialVaultLoadResult loaded = Load();
            if (!loaded.IsReady || !loaded.Encrypted)
                return new(CredentialVaultState.Invalid, "An unlocked encrypted credentials vault is required.");
            byte[] original = File.ReadAllBytes(_vaultPath);
            VaultDocument source = JsonConvert.DeserializeObject<VaultDocument>(Encoding.UTF8.GetString(original))
                ?? throw new JsonException("Invalid credentials document.");
            if (source.Version == 3)
                return new(CredentialVaultState.Ready, "The vault already uses v3.");
            string plain = DecryptPayload(source, _activeKey)
                ?? throw new CryptographicException("The original credentials could not be verified.");
            _ = JArray.Parse(plain);
            var upgraded = new VaultDocument { Version = 3, Encrypted = true, Entries = null, UpdatedUtc = DateTime.UtcNow };
            EncryptDocument(upgraded, plain, _activeKey);
            DurableTextFile.Write(temporary, JsonConvert.SerializeObject(upgraded, Formatting.Indented));
            var staged = JsonConvert.DeserializeObject<VaultDocument>(File.ReadAllText(temporary))!;
            if (DecryptPayload(staged, _activeKey) != plain)
                throw new CryptographicException("The migrated credentials did not pass verification.");
            if (!original.AsSpan().SequenceEqual(File.ReadAllBytes(_vaultPath)))
                return new(CredentialVaultState.Failed, "The source changed during migration. Retry after closing other writers.");
            string backup = _vaultPath + ".pre-v3-" + Guid.NewGuid().ToString("N") + ".bak";
            // Atomically retain the old ciphertext and install the verified replacement.
            File.Replace(temporary, _vaultPath, backup);
            return new(CredentialVaultState.Ready, BackupPath: backup);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or CryptographicException or JsonException or FormatException)
        {
            return new(CredentialVaultState.Failed, "Migration failed; the source was not replaced. Check the key and disk access.");
        }
        finally
        {
            try { File.Delete(temporary); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
        }
    }

    public CredentialVaultWriteResult ImportEncrypted(string sourcePath, string encryptionKey)
    {
        if (!OperatingSystem.IsWindows())
        {
            return new CredentialVaultWriteResult(CredentialVaultState.Failed, "Secure key storage currently requires Windows DPAPI.");
        }

        string temporaryPath = _vaultPath + ".import.tmp";
        byte[]? decodedKey = null;
        try
        {
            string source = Path.GetFullPath(sourcePath);
            if (!File.Exists(source) || string.Equals(source, _vaultPath, StringComparison.OrdinalIgnoreCase))
            {
                return new CredentialVaultWriteResult(CredentialVaultState.Invalid, "Select an existing external credentials.secure.json file.");
            }

            decodedKey = Convert.FromBase64String(encryptionKey.Trim());
            if (decodedKey.Length != 32)
            {
                return new CredentialVaultWriteResult(CredentialVaultState.Invalid, "The encryption key must contain 32 bytes.");
            }

            VaultDocument? document = JsonConvert.DeserializeObject<VaultDocument>(File.ReadAllText(source));
            if (document is null || !document.Encrypted ||
                !(document.Version == 3 && document.Algorithm == AuthenticatedCredentialPayload.Algorithm ||
                  document.Version < 3 && string.Equals(document.Algorithm, "aes-256-cbc", StringComparison.OrdinalIgnoreCase)))
            {
                return new CredentialVaultWriteResult(CredentialVaultState.Invalid, "Only encrypted SDA++ credentials files can be imported.");
            }

            string? payload = DecryptPayload(document, encryptionKey.Trim());
            if (payload is null)
            {
                return new CredentialVaultWriteResult(CredentialVaultState.Locked, "The decryption key is incorrect.");
            }

            List<StoredCredential> imported;
            try
            {
                List<VaultEntry>? entries = JsonConvert.DeserializeObject<List<VaultEntry>>(payload);
                if (entries is null)
                {
                    return new CredentialVaultWriteResult(CredentialVaultState.Invalid, "The encrypted payload is invalid.");
                }
                imported = MapEntries(entries).Where(IsUsableCredential).Select(NormalizeImportedCredential).ToList();
            }
            finally
            {
                payload = string.Empty;
            }

            if (File.Exists(_vaultPath))
            {
                CredentialVaultLoadResult destination = Load();
                if (!destination.IsReady)
                {
                    return new CredentialVaultWriteResult(destination.State, destination.Detail);
                }
                return MergeImportedCredentials(destination, imported);
            }

            Directory.CreateDirectory(Path.GetDirectoryName(_vaultPath)!);
            File.Copy(source, temporaryPath, overwrite: false);
            RememberKey(encryptionKey.Trim());
            File.Move(temporaryPath, _vaultPath, overwrite: false);
            _activeKey = encryptionKey.Trim();
            return new CredentialVaultWriteResult(CredentialVaultState.Ready);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException or FormatException or IOException or UnauthorizedAccessException or JsonException or CryptographicException)
        {
            return new CredentialVaultWriteResult(CredentialVaultState.Failed, exception.Message);
        }
        finally
        {
            if (decodedKey is not null) CryptographicOperations.ZeroMemory(decodedKey);
            try { File.Delete(temporaryPath); }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
        }
    }

    public CredentialVaultWriteResult ImportPlaintext(string sourcePath)
    {
        CredentialVaultLoadResult destination = Load();
        if (!destination.IsReady)
        {
            return new CredentialVaultWriteResult(destination.State, destination.Detail ?? "Create or unlock the destination credentials vault first.");
        }

        try
        {
            string source = Path.GetFullPath(sourcePath);
            if (!File.Exists(source) || string.Equals(source, _vaultPath, StringComparison.OrdinalIgnoreCase))
            {
                return new CredentialVaultWriteResult(CredentialVaultState.Invalid, "Select an external plaintext credentials file.");
            }

            IReadOnlyList<StoredCredential> imported = ParseImportCredentials(source, out string? importError);
            if (imported.Count == 0)
            {
                return new CredentialVaultWriteResult(
                    importError is null ? CredentialVaultState.Invalid : CredentialVaultState.Locked,
                    importError ?? "No valid credentials were found in the selected legacy file.");
            }

            return MergeImportedCredentials(destination, imported);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException or IOException or UnauthorizedAccessException or JsonException)
        {
            return new CredentialVaultWriteResult(CredentialVaultState.Failed, exception.Message);
        }
    }

    public CredentialVaultWriteResult DeleteVault()
    {
        if (!File.Exists(_vaultPath))
        {
            return new CredentialVaultWriteResult(CredentialVaultState.Missing, "The credentials vault does not exist.");
        }

        try
        {
            File.Delete(_vaultPath);
            ForgetRememberedKey();
            _activeKey = string.Empty;
            return new CredentialVaultWriteResult(CredentialVaultState.Ready);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or CryptographicException)
        {
            return new CredentialVaultWriteResult(CredentialVaultState.Failed, exception.Message);
        }
    }

    public CredentialVaultWriteResult Save(VaultAccount account, string username, string password)
    {
        ArgumentNullException.ThrowIfNull(account);
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrEmpty(password))
        {
            return new CredentialVaultWriteResult(CredentialVaultState.Invalid, "Username and password are required.");
        }

        CredentialVaultLoadResult load = Load();
        if (!load.IsReady)
        {
            return new CredentialVaultWriteResult(load.State, load.Detail);
        }

        List<StoredCredential> credentials = load.Credentials.ToList();
        ulong.TryParse(account.SteamId, out ulong steamId);
        int index = credentials.FindIndex(item =>
            (steamId > 0 && item.SteamId == steamId) ||
            string.Equals(item.AccountName, account.Name, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(item.Username, username, StringComparison.OrdinalIgnoreCase));
        var updated = new StoredCredential(steamId, account.Name, username.Trim(), password, DateTime.UtcNow);
        if (index >= 0) credentials[index] = updated;
        else credentials.Add(updated);
        return SaveEntries(credentials, load.Encrypted);
    }

    public CredentialVaultWriteResult Remove(VaultAccount account)
    {
        ArgumentNullException.ThrowIfNull(account);
        CredentialVaultLoadResult load = Load();
        if (!load.IsReady)
        {
            return new CredentialVaultWriteResult(load.State, load.Detail);
        }

        ulong.TryParse(account.SteamId, out ulong steamId);
        List<StoredCredential> credentials = load.Credentials.Where(item =>
            !((steamId > 0 && item.SteamId == steamId) ||
              string.Equals(item.AccountName, account.Name, StringComparison.OrdinalIgnoreCase))).ToList();
        return SaveEntries(credentials, load.Encrypted);
    }

    public CredentialVaultWriteResult Remove(StoredCredential credential)
    {
        ArgumentNullException.ThrowIfNull(credential);
        CredentialVaultLoadResult load = Load();
        if (!load.IsReady) return new CredentialVaultWriteResult(load.State, load.Detail);
        List<StoredCredential> credentials = load.Credentials.Where(item =>
            !((credential.SteamId > 0 && item.SteamId == credential.SteamId) ||
              string.Equals(item.AccountName, credential.AccountName, StringComparison.OrdinalIgnoreCase))).ToList();
        return SaveEntries(credentials, load.Encrypted);
    }

    private CredentialVaultWriteResult SaveEntries(IReadOnlyCollection<StoredCredential> credentials, bool encrypted)
    {
        try
        {
            var document = new VaultDocument
            {
                Version = File.Exists(_vaultPath) && JsonConvert.DeserializeObject<VaultDocument>(File.ReadAllText(_vaultPath))?.Version == 3 ? 3 : 2,
                Encrypted = encrypted,
                UpdatedUtc = DateTime.UtcNow,
                Entries = credentials.OrderBy(item => item.AccountName, StringComparer.OrdinalIgnoreCase)
                    .Select(MapEntry).ToList()
            };
            if (encrypted)
            {
                string key = !string.IsNullOrWhiteSpace(_activeKey)
                    ? _activeKey
                    : LoadRememberedKeys().FirstOrDefault() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(key))
                {
                    return new CredentialVaultWriteResult(CredentialVaultState.Locked, "The credentials key is unavailable.");
                }

                string payload = JsonConvert.SerializeObject(document.Entries, Formatting.None);
                EncryptDocument(document, payload, key);
                payload = string.Empty;
                key = string.Empty;
                document.Entries = null;
            }
            else
            {
                document.Algorithm = "none";
            }

            string temporaryPath = _vaultPath + ".tmp";
            File.WriteAllText(temporaryPath, JsonConvert.SerializeObject(document, Formatting.Indented));
            File.Move(temporaryPath, _vaultPath, true);
            return new CredentialVaultWriteResult(CredentialVaultState.Ready);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or CryptographicException)
        {
            return new CredentialVaultWriteResult(CredentialVaultState.Failed, exception.Message);
        }
    }

    private CredentialVaultWriteResult MergeImportedCredentials(
        CredentialVaultLoadResult destination,
        IReadOnlyCollection<StoredCredential> imported)
    {
        List<StoredCredential> merged = destination.Credentials.ToList();
        foreach (StoredCredential credential in imported)
        {
            int index = merged.FindIndex(item =>
                (credential.SteamId > 0 && item.SteamId == credential.SteamId) ||
                string.Equals(item.AccountName, credential.AccountName, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(item.Username, credential.Username, StringComparison.OrdinalIgnoreCase));
            if (index >= 0) merged[index] = credential;
            else merged.Add(credential);
        }

        CredentialVaultWriteResult saved = SaveEntries(merged, destination.Encrypted);
        return saved.IsSuccess
            ? new CredentialVaultWriteResult(CredentialVaultState.Ready, $"Imported {imported.Count} credential entries.")
            : saved;
    }

    private IReadOnlyList<string> LoadRememberedKeys()
    {
        return LoadRememberedKeys(_secretPath, _vaultPath);
    }

    private static IReadOnlyList<string> LoadRememberedKeys(string secretPath, string vaultPath)
    {
        if (!OperatingSystem.IsWindows() || !File.Exists(secretPath)) return [];
        try
        {
            SecretDocument? document = JsonConvert.DeserializeObject<SecretDocument>(File.ReadAllText(secretPath));
            string secretName = GetKeySecretName(vaultPath);
            if (document?.Values is null) return [];
            IEnumerable<KeyValuePair<string, string>> candidates = document.Values
                .Where(item => item.Key.StartsWith("credentials-file-key-", StringComparison.Ordinal))
                .OrderByDescending(item => string.Equals(item.Key, secretName, StringComparison.Ordinal));
            var keys = new List<string>();
            foreach (KeyValuePair<string, string> candidate in candidates)
            {
                if (string.IsNullOrWhiteSpace(candidate.Value)) continue;
                try
                {
                    byte[] encrypted = Convert.FromBase64String(candidate.Value);
                    byte[] decrypted = ProtectedData.Unprotect(encrypted, SecretEntropy, DataProtectionScope.CurrentUser);
                    try { keys.Add(Encoding.UTF8.GetString(decrypted)); }
                    finally
                    {
                        CryptographicOperations.ZeroMemory(encrypted);
                        CryptographicOperations.ZeroMemory(decrypted);
                    }
                }
                catch (Exception exception) when (exception is FormatException or CryptographicException)
                {
                }
            }
            return keys;
        }
        catch
        {
            return [];
        }
    }

    [SupportedOSPlatform("windows")]
    private void RememberKey(string key)
    {
        SecretDocument document;
        try
        {
            document = File.Exists(_secretPath)
                ? JsonConvert.DeserializeObject<SecretDocument>(File.ReadAllText(_secretPath)) ?? new SecretDocument()
                : new SecretDocument();
        }
        catch (JsonException)
        {
            document = new SecretDocument();
        }

        byte[] plain = Encoding.UTF8.GetBytes(key);
        byte[] protectedKey = ProtectedData.Protect(plain, SecretEntropy, DataProtectionScope.CurrentUser);
        try
        {
            document.Values[GetKeySecretName(_vaultPath)] = Convert.ToBase64String(protectedKey);
            string temporaryPath = _secretPath + ".tmp";
            File.WriteAllText(temporaryPath, JsonConvert.SerializeObject(document, Formatting.Indented));
            File.Move(temporaryPath, _secretPath, overwrite: true);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plain);
            CryptographicOperations.ZeroMemory(protectedKey);
        }
    }

    private void ForgetRememberedKey()
    {
        if (!File.Exists(_secretPath)) return;
        SecretDocument? document = JsonConvert.DeserializeObject<SecretDocument>(File.ReadAllText(_secretPath));
        if (document?.Values is null || !document.Values.Remove(GetKeySecretName(_vaultPath))) return;

        string temporaryPath = _secretPath + ".tmp";
        File.WriteAllText(temporaryPath, JsonConvert.SerializeObject(document, Formatting.Indented));
        File.Move(temporaryPath, _secretPath, overwrite: true);
    }

    private static string GetKeySecretName(string path)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(path.ToLowerInvariant()));
        return "credentials-file-key-" + Convert.ToBase64String(hash).Replace("/", "_").Replace("+", "-");
    }

    private static void EncryptDocument(VaultDocument document, string payload, string encryptionKey)
    {
        if (document.Version == 3)
        {
            byte[] authenticatedPlain = Encoding.UTF8.GetBytes(payload);
            try
            {
                (document.Iv, document.Payload) = AuthenticatedCredentialPayload.Encrypt(authenticatedPlain, encryptionKey);
                document.Algorithm = AuthenticatedCredentialPayload.Algorithm;
            }
            finally { CryptographicOperations.ZeroMemory(authenticatedPlain); }
            return;
        }
        byte[] key = NormalizeKey(encryptionKey);
        byte[] iv = RandomNumberGenerator.GetBytes(16);
        byte[] plain = Encoding.UTF8.GetBytes(payload);
        try
        {
            using Aes aes = Aes.Create();
            aes.KeySize = 256;
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.PKCS7;
            aes.Key = key;
            aes.IV = iv;
            document.Algorithm = "aes-256-cbc";
            document.Iv = Convert.ToBase64String(iv);
            document.Payload = Convert.ToBase64String(aes.CreateEncryptor().TransformFinalBlock(plain, 0, plain.Length));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
            CryptographicOperations.ZeroMemory(iv);
            CryptographicOperations.ZeroMemory(plain);
        }
    }

    private static string? DecryptPayload(VaultDocument document, string encryptionKey)
    {
        if (string.IsNullOrWhiteSpace(document.Payload) || string.IsNullOrWhiteSpace(document.Iv)) return null;
        if (document.Version >= 3 || document.Algorithm == AuthenticatedCredentialPayload.Algorithm)
        {
            try
            {
                byte[] plain = AuthenticatedCredentialPayload.Decrypt(document.Version, document.Algorithm, document.Iv, document.Payload, encryptionKey);
                try { return Encoding.UTF8.GetString(plain); }
                finally { CryptographicOperations.ZeroMemory(plain); }
            }
            catch (Exception error) when (error is CryptographicException or FormatException) { return null; }
        }
        if (!string.Equals(document.Algorithm, "aes-256-cbc", StringComparison.OrdinalIgnoreCase)) return null;
        byte[] key = NormalizeKey(encryptionKey);
        byte[] iv = Convert.FromBase64String(document.Iv);
        byte[] cipher = Convert.FromBase64String(document.Payload);
        try
        {
            using Aes aes = Aes.Create();
            aes.KeySize = 256;
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.PKCS7;
            aes.Key = key;
            aes.IV = iv;
            byte[] plain = aes.CreateDecryptor().TransformFinalBlock(cipher, 0, cipher.Length);
            try { return Encoding.UTF8.GetString(plain); }
            finally { CryptographicOperations.ZeroMemory(plain); }
        }
        catch (CryptographicException) { return null; }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
            CryptographicOperations.ZeroMemory(iv);
            CryptographicOperations.ZeroMemory(cipher);
        }
    }

    private static byte[] NormalizeKey(string encryptionKey)
    {
        try
        {
            byte[] decoded = Convert.FromBase64String(encryptionKey.Trim());
            if (decoded.Length == 32) return decoded;
            byte[] hash = SHA256.HashData(decoded);
            CryptographicOperations.ZeroMemory(decoded);
            return hash;
        }
        catch (FormatException)
        {
            return SHA256.HashData(Encoding.UTF8.GetBytes(encryptionKey.Trim()));
        }
    }

    private CredentialVaultLoadResult Failed(CredentialVaultState state, string detail) =>
        new(state, [], _vaultPath, true, detail);

    private static IReadOnlyList<StoredCredential> MapEntries(IEnumerable<VaultEntry>? entries) =>
        (entries ?? []).Select(item => new StoredCredential(
            item.SteamId,
            item.AccountName ?? string.Empty,
            item.Username ?? string.Empty,
            item.Password ?? string.Empty,
            item.UpdatedUtc)).ToArray();

    private static VaultEntry MapEntry(StoredCredential item) => new()
    {
        SteamId = item.SteamId,
        AccountName = item.AccountName,
        Username = item.Username,
        Password = item.Password,
        UpdatedUtc = item.UpdatedUtc
    };

    private static IReadOnlyList<StoredCredential> ParseImportCredentials(string sourcePath, out string? error)
    {
        error = null;
        string contents = File.ReadAllText(sourcePath);
        try
        {
            JToken json = JToken.Parse(contents);
            if (json is not JObject root)
            {
                IReadOnlyList<StoredCredential> genericJson = ParseGenericJsonCredentials(json);
                if (genericJson.Count > 0) return genericJson;
                error = "No valid login and password pairs were found in the JSON file.";
                return [];
            }

            if (root.Value<bool?>("encrypted") == true)
            {
                VaultDocument? encryptedDocument = root.ToObject<VaultDocument>();
                IReadOnlyList<string> keys = LoadRememberedKeys(
                    Path.Combine(Path.GetDirectoryName(sourcePath)!, "cloud.secret.json"),
                    sourcePath);
                foreach (string key in keys)
                {
                    string? payload = encryptedDocument is null ? null : DecryptPayload(encryptedDocument, key);
                    if (payload is null) continue;
                    try
                    {
                        List<VaultEntry>? entries = JsonConvert.DeserializeObject<List<VaultEntry>>(payload);
                        IReadOnlyList<StoredCredential> credentials = MapEntries(entries)
                            .Where(IsUsableCredential)
                            .Select(NormalizeImportedCredential)
                            .ToArray();
                        if (credentials.Count > 0) return credentials;
                    }
                    finally
                    {
                        payload = string.Empty;
                    }
                }

                error = "The encrypted legacy vault could not be unlocked. Keep its original cloud.secret.json beside credentials.secure.json and import it under the same Windows user.";
                return [];
            }

            if (root["version"] is not null)
            {
                VaultDocument? document = root.ToObject<VaultDocument>();
                return MapEntries(document?.Entries)
                    .Where(IsUsableCredential)
                    .Select(NormalizeImportedCredential)
                    .ToArray();
            }

            IReadOnlyList<StoredCredential> legacy = ParseLegacyDpapiCredentials(root);
            if (legacy.Count > 0) return legacy;

            IReadOnlyList<StoredCredential> generic = ParseGenericJsonCredentials(root);
            if (generic.Count > 0) return generic;
        }
        catch (JsonException)
        {
        }

        var result = new List<StoredCredential>();
        foreach (string rawLine in File.ReadLines(sourcePath))
        {
            string line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;
            int separator = line.IndexOf(':');
            if (separator <= 0 || separator == line.Length - 1) continue;
            string username = line[..separator].Trim();
            string password = line[(separator + 1)..];
            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password)) continue;
            result.Add(new StoredCredential(0, username, username, password, DateTime.UtcNow));
        }
        return result;
    }

    private static IReadOnlyList<StoredCredential> ParseGenericJsonCredentials(JToken root)
    {
        var result = new List<StoredCredential>();
        IEnumerable<JToken> candidates = root switch
        {
            JArray array => array.Children(),
            JObject container when FindCredentialArray(container) is { } array => array.Children(),
            JObject single when HasCredentialFields(single) => [single],
            JObject map => map.Properties()
                .Where(property => property.Value.Type == JTokenType.String)
                .Select(property => new JObject
                {
                    ["login"] = property.Name,
                    ["password"] = property.Value.Value<string>()
                }),
            _ => []
        };

        foreach (JToken candidate in candidates)
        {
            if (candidate.Type == JTokenType.String)
            {
                string pair = candidate.Value<string>() ?? string.Empty;
                int separator = pair.IndexOf(':');
                if (separator <= 0 || separator == pair.Length - 1) continue;

                string pairUsername = pair[..separator].Trim();
                string pairPassword = pair[(separator + 1)..];
                if (string.IsNullOrWhiteSpace(pairUsername) || string.IsNullOrWhiteSpace(pairPassword)) continue;

                result.Add(new StoredCredential(
                    0,
                    pairUsername,
                    pairUsername,
                    pairPassword,
                    DateTime.UtcNow));
                continue;
            }

            if (candidate is not JObject item) continue;

            string username = FirstString(item, "login", "username", "user", "steam_login");
            string password = FirstString(item, "password", "pass", "steam_password");
            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrEmpty(password)) continue;

            string accountName = FirstString(item, "account_name", "accountName", "account", "name");
            ulong steamId = FirstUInt64(item, "steam_id", "steamId", "steamid");
            result.Add(new StoredCredential(
                steamId,
                string.IsNullOrWhiteSpace(accountName) ? username.Trim() : accountName.Trim(),
                username.Trim(),
                password,
                DateTime.UtcNow));
        }

        return result;
    }

    private static JArray? FindCredentialArray(JObject root) =>
        new[] { "credentials", "accounts", "entries", "logins" }
            .Select(name => root.GetValue(name, StringComparison.OrdinalIgnoreCase))
            .OfType<JArray>()
            .FirstOrDefault();

    private static bool HasCredentialFields(JObject value) =>
        !string.IsNullOrWhiteSpace(FirstString(value, "login", "username", "user", "steam_login")) &&
        !string.IsNullOrEmpty(FirstString(value, "password", "pass", "steam_password"));

    private static string FirstString(JObject value, params string[] names)
    {
        foreach (string name in names)
        {
            string? text = value.GetValue(name, StringComparison.OrdinalIgnoreCase)?.Value<string>();
            if (!string.IsNullOrEmpty(text)) return text;
        }

        return string.Empty;
    }

    private static ulong FirstUInt64(JObject value, params string[] names)
    {
        foreach (string name in names)
        {
            if (ulong.TryParse(
                    value.GetValue(name, StringComparison.OrdinalIgnoreCase)?.ToString(),
                    out ulong steamId))
            {
                return steamId;
            }
        }

        return 0;
    }

    private static IReadOnlyList<StoredCredential> ParseLegacyDpapiCredentials(JObject root)
    {
        if (!OperatingSystem.IsWindows() || root["entries"] is not JArray entries) return [];
        var result = new List<StoredCredential>();
        foreach (JObject item in entries.OfType<JObject>())
        {
            string? username = TryUnprotectLegacyCredential(item.Value<string>("username"));
            string? password = TryUnprotectLegacyCredential(item.Value<string>("password"));
            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrEmpty(password)) continue;
            result.Add(new StoredCredential(
                item.Value<ulong?>("steam_id") ?? 0,
                username,
                username,
                password,
                DateTime.UtcNow));
        }
        return result;
    }

    [SupportedOSPlatform("windows")]
    private static string? TryUnprotectLegacyCredential(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        byte[]? protectedBytes = null;
        byte[]? plainBytes = null;
        try
        {
            protectedBytes = Convert.FromBase64String(value);
            plainBytes = ProtectedData.Unprotect(protectedBytes, LegacyCredentialEntropy, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(plainBytes);
        }
        catch (Exception exception) when (exception is FormatException or CryptographicException)
        {
            return null;
        }
        finally
        {
            if (protectedBytes is not null) CryptographicOperations.ZeroMemory(protectedBytes);
            if (plainBytes is not null) CryptographicOperations.ZeroMemory(plainBytes);
        }
    }

    private static bool IsUsableCredential(StoredCredential credential) =>
        !string.IsNullOrWhiteSpace(credential.Username) && !string.IsNullOrEmpty(credential.Password);

    private static bool IdentityEquals(string? left, string? right) =>
        !string.IsNullOrWhiteSpace(left) &&
        !string.IsNullOrWhiteSpace(right) &&
        string.Equals(left.Trim(), right.Trim(), StringComparison.OrdinalIgnoreCase);

    private static StoredCredential NormalizeImportedCredential(StoredCredential credential) => credential with
    {
        AccountName = string.IsNullOrWhiteSpace(credential.AccountName) ? credential.Username.Trim() : credential.AccountName.Trim(),
        Username = credential.Username.Trim(),
        UpdatedUtc = DateTime.UtcNow
    };

    private sealed class VaultDocument
    {
        [JsonProperty("version")] public int Version { get; set; } = 2;
        [JsonProperty("encrypted")] public bool Encrypted { get; set; }
        [JsonProperty("algorithm")] public string Algorithm { get; set; } = "none";
        [JsonProperty("iv")] public string? Iv { get; set; }
        [JsonProperty("payload")] public string? Payload { get; set; }
        [JsonProperty("entries")] public List<VaultEntry>? Entries { get; set; } = [];
        [JsonProperty("updated_utc")] public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;
    }

    private sealed class VaultEntry
    {
        [JsonProperty("steam_id")] public ulong SteamId { get; set; }
        [JsonProperty("account_name")] public string? AccountName { get; set; }
        [JsonProperty("username")] public string? Username { get; set; }
        [JsonProperty("password")] public string? Password { get; set; }
        [JsonProperty("updated_utc")] public DateTime UpdatedUtc { get; set; }
    }

    private sealed class SecretDocument
    {
        [JsonProperty("values")] public Dictionary<string, string> Values { get; set; } = [];
    }
}
