using SDAPlusPlus.Core.Credentials;
using SDAPlusPlus.Core.Vault;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using SteamAuth;

namespace SDAPlusPlus.Core.Tests;

public sealed class LegacyStoredCredentialVaultTests
{
    [Fact]
    public void UpgradeCreatesExactEncryptedBackupAndIsIdempotent()
    {
        if (!OperatingSystem.IsWindows()) return;
        string directory = CreateDirectory();
        try
        {
            string key = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
            var vault = new LegacyStoredCredentialVault(directory);
            Assert.True(vault.CreateEncrypted(key).IsSuccess);
            var account = new VaultAccount("test", "76561198000000000", new SteamGuardAccount(), false, false);
            Assert.True(vault.Save(account, "test", "synthetic-secret").IsSuccess);
            string path = Path.Combine(directory, "credentials.secure.json");
            byte[] original = File.ReadAllBytes(path);
            var result = vault.UpgradeEncryption();
            Assert.True(result.IsSuccess, result.Detail);
            Assert.NotNull(result.BackupPath);
            Assert.Equal(original, File.ReadAllBytes(result.BackupPath!));
            var restored = new LegacyStoredCredentialVault(Path.Combine(directory, "restore-test"));
            Assert.True(restored.ImportEncrypted(result.BackupPath!, key).IsSuccess);
            Assert.Equal("synthetic-secret", Assert.Single(restored.Load().Credentials).Password);
            Assert.Equal(3, Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(path)).Value<int>("version"));
            Assert.Equal("synthetic-secret", Assert.Single(vault.Load().Credentials).Password);
            byte[] upgraded = File.ReadAllBytes(path);
            Assert.Null(vault.UpgradeEncryption().BackupPath);
            Assert.Equal(upgraded, File.ReadAllBytes(path));
            Assert.Single(Directory.GetFiles(directory, "*.bak"));
            Assert.Empty(Directory.GetFiles(directory, "*.tmp"));
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public void MigrationRefusesUnreadableSourceWithoutReplacingIt()
    {
        string directory = CreateDirectory();
        try
        {
            string path = Path.Combine(directory, "credentials.secure.json");
            File.WriteAllText(path, "invalid-test-document");
            var result = new LegacyStoredCredentialVault(directory).UpgradeEncryption();
            Assert.False(result.IsSuccess);
            Assert.Equal("invalid-test-document", File.ReadAllText(path));
            Assert.Empty(Directory.GetFiles(directory, "*.bak"));
            Assert.Empty(Directory.GetFiles(directory, "*.tmp"));
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public void V3LoadAndSavePreserveAuthenticatedFormatAndTamperingBlocksWrite()
    {
        if (!OperatingSystem.IsWindows()) return;
        string directory = CreateDirectory();
        try
        {
            string key = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
            var vault = new LegacyStoredCredentialVault(directory);
            Assert.True(vault.CreateEncrypted(key).IsSuccess);
            var encrypted = AuthenticatedCredentialPayload.Encrypt("[]"u8.ToArray(), key);
            var envelope = new Newtonsoft.Json.Linq.JObject
            {
                ["version"] = 3, ["encrypted"] = true, ["algorithm"] = "aes-256-gcm",
                ["iv"] = encrypted.Iv, ["payload"] = encrypted.Payload
            };
            string path = Path.Combine(directory, "credentials.secure.json");
            File.WriteAllText(path, envelope.ToString());
            Assert.True(vault.Load().IsReady);
            var account = new VaultAccount("test", "76561198000000000", new SteamGuardAccount(), false, false);
            Assert.True(vault.Save(account, "test", "synthetic-password").IsSuccess);
            envelope = Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(path));
            Assert.Equal(3, envelope.Value<int>("version"));
            Assert.Equal("aes-256-gcm", envelope.Value<string>("algorithm"));
            Assert.Equal("synthetic-password", Assert.Single(vault.Load().Credentials).Password);
            byte[] corrupted = Convert.FromBase64String(envelope.Value<string>("payload")!);
            corrupted[0] ^= 1;
            envelope["payload"] = Convert.ToBase64String(corrupted);
            File.WriteAllText(path, envelope.ToString());
            string before = File.ReadAllText(path);
            Assert.False(vault.Save(account, "test", "replacement").IsSuccess);
            Assert.Equal(before, File.ReadAllText(path));
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public void Load_WhenFileDoesNotExist_ReturnsMissingWithoutCreatingFiles()
    {
        string directory = CreateDirectory();
        try
        {
            var vault = new LegacyStoredCredentialVault(directory);

            CredentialVaultLoadResult result = vault.Load();

            Assert.Equal(CredentialVaultState.Missing, result.State);
            Assert.Empty(result.Credentials);
            Assert.Empty(Directory.GetFiles(directory));
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void Load_ReadsCompatiblePlaintextDocumentWithoutExposingItInStatus()
    {
        string directory = CreateDirectory();
        try
        {
            File.WriteAllText(Path.Combine(directory, "credentials.secure.json"), """
                {
                  "version": 2,
                  "encrypted": false,
                  "algorithm": "none",
                  "entries": [
                    {
                      "steam_id": 76561198000000000,
                      "account_name": "test-account",
                      "username": "test-login",
                      "password": "test-password",
                      "updated_utc": "2026-01-01T00:00:00Z"
                    }
                  ]
                }
                """);
            var vault = new LegacyStoredCredentialVault(directory);

            CredentialVaultLoadResult result = vault.Load();

            Assert.True(result.IsReady);
            Assert.False(result.Encrypted);
            StoredCredential credential = Assert.Single(result.Credentials);
            Assert.Equal("test-account", credential.AccountName);
            Assert.Equal("test-login", credential.Username);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void Load_AcceptsDpapiKeyFromPreviousPortablePath()
    {
        if (!OperatingSystem.IsWindows()) return;
        string directory = CreateDirectory();
        try
        {
            string key = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
            byte[] iv = RandomNumberGenerator.GetBytes(16);
            string payload = """[{"steam_id":76561198000000000,"account_name":"moved","username":"moved","password":"secret","updated_utc":"2026-01-01T00:00:00Z"}]""";
            byte[] cipher;
            using (Aes aes = Aes.Create())
            {
                aes.Key = Convert.FromBase64String(key);
                aes.IV = iv;
                cipher = aes.CreateEncryptor().TransformFinalBlock(Encoding.UTF8.GetBytes(payload), 0, Encoding.UTF8.GetByteCount(payload));
            }
            File.WriteAllText(Path.Combine(directory, "credentials.secure.json"), JsonConvert.SerializeObject(new
            {
                version = 2,
                encrypted = true,
                algorithm = "aes-256-cbc",
                iv = Convert.ToBase64String(iv),
                payload = Convert.ToBase64String(cipher)
            }));
            byte[] protectedKey = ProtectedData.Protect(
                Encoding.UTF8.GetBytes(key),
                Encoding.UTF8.GetBytes("SDA++ Cloud Secret Store"),
                DataProtectionScope.CurrentUser);
            File.WriteAllText(Path.Combine(directory, "cloud.secret.json"), JsonConvert.SerializeObject(new
            {
                values = new Dictionary<string, string>
                {
                    ["credentials-file-key-from-an-old-path"] = Convert.ToBase64String(protectedKey)
                }
            }));

            CredentialVaultLoadResult result = new LegacyStoredCredentialVault(directory).Load();

            Assert.True(result.IsReady);
            Assert.True(result.Encrypted);
            Assert.Single(result.Credentials);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void CreateEncrypted_CreatesEmptyVaultThatCanBeReopened()
    {
        if (!OperatingSystem.IsWindows()) return;
        string directory = CreateDirectory();
        try
        {
            string key = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
            var vault = new LegacyStoredCredentialVault(directory);

            CredentialVaultWriteResult created = vault.CreateEncrypted(key);
            CredentialVaultLoadResult reopened = new LegacyStoredCredentialVault(directory).Load();

            Assert.True(created.IsSuccess);
            Assert.True(reopened.IsReady);
            Assert.True(reopened.Encrypted);
            Assert.Empty(reopened.Credentials);
            Assert.True(File.Exists(Path.Combine(directory, "cloud.secret.json")));
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void ImportEncrypted_ValidatesKeyAndPreservesSource()
    {
        if (!OperatingSystem.IsWindows()) return;
        string sourceDirectory = CreateDirectory();
        string destinationDirectory = CreateDirectory();
        try
        {
            string key = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
            var sourceVault = new LegacyStoredCredentialVault(sourceDirectory);
            Assert.True(sourceVault.CreateEncrypted(key).IsSuccess);
            string sourcePath = Path.Combine(sourceDirectory, "credentials.secure.json");
            string sourceBefore = File.ReadAllText(sourcePath);

            var destinationVault = new LegacyStoredCredentialVault(destinationDirectory);
            CredentialVaultWriteResult wrong = destinationVault.ImportEncrypted(sourcePath, Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)));
            CredentialVaultWriteResult imported = destinationVault.ImportEncrypted(sourcePath, key);

            Assert.Equal(CredentialVaultState.Locked, wrong.State);
            Assert.True(imported.IsSuccess);
            Assert.True(destinationVault.Load().IsReady);
            Assert.Equal(sourceBefore, File.ReadAllText(sourcePath));
        }
        finally
        {
            Directory.Delete(sourceDirectory, true);
            Directory.Delete(destinationDirectory, true);
        }
    }

    [Fact]
    public void ImportPlaintext_MergesLegacyPairsIntoEncryptedVaultAndPreservesSource()
    {
        if (!OperatingSystem.IsWindows()) return;
        string directory = CreateDirectory();
        string sourcePath = Path.Combine(CreateDirectory(), "accounts.txt");
        try
        {
            const string sourceContents = "# old SDA++ export\nfirst:password:with:colons\nsecond:other-password\n";
            File.WriteAllText(sourcePath, sourceContents);
            var vault = new LegacyStoredCredentialVault(directory);
            Assert.True(vault.CreateEncrypted(Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))).IsSuccess);

            CredentialVaultWriteResult imported = vault.ImportPlaintext(sourcePath);
            CredentialVaultLoadResult reopened = vault.Load();

            Assert.True(imported.IsSuccess);
            Assert.True(reopened.Encrypted);
            Assert.Equal(2, reopened.Credentials.Count);
            Assert.Equal("password:with:colons", reopened.Credentials.Single(item => item.Username == "first").Password);
            Assert.Equal(sourceContents, File.ReadAllText(sourcePath));
        }
        finally
        {
            Directory.Delete(directory, true);
            Directory.Delete(Path.GetDirectoryName(sourcePath)!, true);
        }
    }

    [Fact]
    public void ImportPlaintext_AcceptsVersion2UnencryptedJson()
    {
        if (!OperatingSystem.IsWindows()) return;
        string directory = CreateDirectory();
        string sourcePath = Path.Combine(CreateDirectory(), "credentials.secure.json");
        try
        {
            File.WriteAllText(sourcePath, """
                {
                  "version": 2,
                  "encrypted": false,
                  "algorithm": "none",
                  "entries": [
                    {
                      "steam_id": 76561198000000001,
                      "account_name": "legacy-account",
                      "username": "legacy-login",
                      "password": "legacy-password",
                      "updated_utc": "2025-01-01T00:00:00Z"
                    }
                  ]
                }
                """);
            var vault = new LegacyStoredCredentialVault(directory);
            Assert.True(vault.CreateEncrypted(Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))).IsSuccess);

            CredentialVaultWriteResult imported = vault.ImportPlaintext(sourcePath);
            StoredCredential credential = Assert.Single(vault.Load().Credentials);

            Assert.True(imported.IsSuccess);
            Assert.Equal("legacy-account", credential.AccountName);
            Assert.Equal("legacy-login", credential.Username);
        }
        finally
        {
            Directory.Delete(directory, true);
            Directory.Delete(Path.GetDirectoryName(sourcePath)!, true);
        }
    }

    [Fact]
    public void ImportPlaintext_AcceptsGenericJsonArray()
    {
        if (!OperatingSystem.IsWindows()) return;
        string directory = CreateDirectory();
        string sourcePath = Path.Combine(CreateDirectory(), "accounts.json");
        try
        {
            File.WriteAllText(sourcePath, """
                [
                  {
                    "login": "first-login",
                    "password": "first-password",
                    "accountName": "first-account",
                    "steamId": "76561198000000101"
                  },
                  {
                    "username": "second-login",
                    "pass": "second-password"
                  }
                ]
                """);
            var vault = new LegacyStoredCredentialVault(directory);
            Assert.True(vault.CreateEncrypted(Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))).IsSuccess);

            CredentialVaultWriteResult imported = vault.ImportPlaintext(sourcePath);
            CredentialVaultLoadResult reopened = vault.Load();

            Assert.True(imported.IsSuccess);
            Assert.Equal(2, reopened.Credentials.Count);
            Assert.Contains(reopened.Credentials, item =>
                item.AccountName == "first-account" &&
                item.Username == "first-login" &&
                item.SteamId == 76561198000000101);
            Assert.Contains(reopened.Credentials, item =>
                item.AccountName == "second-login" &&
                item.Username == "second-login" &&
                item.Password == "second-password");
        }
        finally
        {
            Directory.Delete(directory, true);
            Directory.Delete(Path.GetDirectoryName(sourcePath)!, true);
        }
    }

    [Fact]
    public void ImportPlaintext_AcceptsJsonArrayOfLoginPasswordStrings()
    {
        if (!OperatingSystem.IsWindows()) return;
        string directory = CreateDirectory();
        string sourcePath = Path.Combine(CreateDirectory(), "accounts.json");
        try
        {
            File.WriteAllText(sourcePath, """
                [
                  "first-login:first-password",
                  "second-login:password:with:colons",
                  "third-login:third-password",
                ]
                """);
            var vault = new LegacyStoredCredentialVault(directory);
            Assert.True(vault.CreateEncrypted(Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))).IsSuccess);

            CredentialVaultWriteResult imported = vault.ImportPlaintext(sourcePath);
            CredentialVaultLoadResult reopened = vault.Load();

            Assert.True(imported.IsSuccess);
            Assert.Equal(3, reopened.Credentials.Count);
            Assert.Contains(reopened.Credentials, item =>
                item.AccountName == "first-login" &&
                item.Username == "first-login" &&
                item.Password == "first-password");
            Assert.Contains(reopened.Credentials, item =>
                item.AccountName == "second-login" &&
                item.Username == "second-login" &&
                item.Password == "password:with:colons");
            Assert.Contains(reopened.Credentials, item =>
                item.AccountName == "third-login" &&
                item.Username == "third-login" &&
                item.Password == "third-password");
        }
        finally
        {
            Directory.Delete(directory, true);
            Directory.Delete(Path.GetDirectoryName(sourcePath)!, true);
        }
    }

    [Fact]
    public void TryGet_LinksImportedLoginPasswordPairToMaFileAccount()
    {
        if (!OperatingSystem.IsWindows()) return;
        string directory = CreateDirectory();
        string sourcePath = Path.Combine(CreateDirectory(), "accounts.json");
        try
        {
            File.WriteAllText(sourcePath, """["  TestLogin:stored-password"]""");
            var vault = new LegacyStoredCredentialVault(directory);
            Assert.True(vault.CreateEncrypted(Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))).IsSuccess);
            Assert.True(vault.ImportPlaintext(sourcePath).IsSuccess);
            var account = new VaultAccount(
                "testlogin ",
                "76561198000000001",
                new SteamGuardAccount { AccountName = "testlogin" },
                false,
                false);

            bool found = vault.TryGet(account, out StoredCredential? credential);

            Assert.True(found);
            Assert.NotNull(credential);
            Assert.Equal("TestLogin", credential.Username);
            Assert.Equal("stored-password", credential.Password);
        }
        finally
        {
            Directory.Delete(directory, true);
            Directory.Delete(Path.GetDirectoryName(sourcePath)!, true);
        }
    }

    [Fact]
    public void ImportPlaintext_AcceptsJsonLoginPasswordMap()
    {
        if (!OperatingSystem.IsWindows()) return;
        string directory = CreateDirectory();
        string sourcePath = Path.Combine(CreateDirectory(), "accounts.json");
        try
        {
            File.WriteAllText(sourcePath, """
                {
                  "first-login": "first-password",
                  "second-login": "second-password"
                }
                """);
            var vault = new LegacyStoredCredentialVault(directory);
            Assert.True(vault.CreateEncrypted(Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))).IsSuccess);

            CredentialVaultWriteResult imported = vault.ImportPlaintext(sourcePath);
            CredentialVaultLoadResult reopened = vault.Load();

            Assert.True(imported.IsSuccess);
            Assert.Equal(2, reopened.Credentials.Count);
            Assert.Contains(reopened.Credentials, item =>
                item.Username == "first-login" && item.Password == "first-password");
            Assert.Contains(reopened.Credentials, item =>
                item.Username == "second-login" && item.Password == "second-password");
        }
        finally
        {
            Directory.Delete(directory, true);
            Directory.Delete(Path.GetDirectoryName(sourcePath)!, true);
        }
    }

    [Fact]
    public void ImportPlaintext_MigratesLegacyDpapiCredentialsJsonForCurrentWindowsUser()
    {
        if (!OperatingSystem.IsWindows()) return;
        string directory = CreateDirectory();
        string sourcePath = Path.Combine(CreateDirectory(), "credentials.json");
        byte[] entropy = Encoding.UTF8.GetBytes("SDA++ by Manewreck Credential Vault");
        byte[] protectedUsername = ProtectedData.Protect(Encoding.UTF8.GetBytes("old-login"), entropy, DataProtectionScope.CurrentUser);
        byte[] protectedPassword = ProtectedData.Protect(Encoding.UTF8.GetBytes("old-password"), entropy, DataProtectionScope.CurrentUser);
        try
        {
            File.WriteAllText(sourcePath, JsonConvert.SerializeObject(new
            {
                entries = new[]
                {
                    new
                    {
                        steam_id = 76561198000000002,
                        username = Convert.ToBase64String(protectedUsername),
                        password = Convert.ToBase64String(protectedPassword),
                        updated_utc = DateTime.UtcNow
                    }
                }
            }));
            var vault = new LegacyStoredCredentialVault(directory);
            Assert.True(vault.CreateEncrypted(Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))).IsSuccess);

            CredentialVaultWriteResult imported = vault.ImportPlaintext(sourcePath);
            StoredCredential credential = Assert.Single(vault.Load().Credentials);

            Assert.True(imported.IsSuccess);
            Assert.Equal("old-login", credential.Username);
            Assert.Equal("old-password", credential.Password);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(entropy);
            CryptographicOperations.ZeroMemory(protectedUsername);
            CryptographicOperations.ZeroMemory(protectedPassword);
            Directory.Delete(directory, true);
            Directory.Delete(Path.GetDirectoryName(sourcePath)!, true);
        }
    }

    [Fact]
    public void ImportPlaintext_AutomaticallyUsesSiblingSecretForEncryptedLegacyVault()
    {
        if (!OperatingSystem.IsWindows()) return;
        string sourceDirectory = CreateDirectory();
        string destinationDirectory = CreateDirectory();
        try
        {
            var sourceVault = new LegacyStoredCredentialVault(sourceDirectory);
            Assert.True(sourceVault.CreateEncrypted(Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))).IsSuccess);
            string pairFile = Path.Combine(sourceDirectory, "pairs.txt");
            File.WriteAllText(pairFile, "migrated-login:migrated-password");
            Assert.True(sourceVault.ImportPlaintext(pairFile).IsSuccess);

            var destinationVault = new LegacyStoredCredentialVault(destinationDirectory);
            Assert.True(destinationVault.CreateEncrypted(Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))).IsSuccess);

            CredentialVaultWriteResult imported = destinationVault.ImportPlaintext(
                Path.Combine(sourceDirectory, "credentials.secure.json"));
            StoredCredential credential = Assert.Single(destinationVault.Load().Credentials);

            Assert.True(imported.IsSuccess);
            Assert.Equal("migrated-login", credential.Username);
            Assert.Equal("migrated-password", credential.Password);
        }
        finally
        {
            Directory.Delete(sourceDirectory, true);
            Directory.Delete(destinationDirectory, true);
        }
    }

    [Fact]
    public void ImportEncrypted_MergesIntoExistingEncryptedVault()
    {
        if (!OperatingSystem.IsWindows()) return;
        string sourceDirectory = CreateDirectory();
        string destinationDirectory = CreateDirectory();
        try
        {
            string sourceKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
            var sourceVault = new LegacyStoredCredentialVault(sourceDirectory);
            Assert.True(sourceVault.CreateEncrypted(sourceKey).IsSuccess);
            string sourcePairs = Path.Combine(sourceDirectory, "source.txt");
            File.WriteAllText(sourcePairs, "source-account:source-password");
            Assert.True(sourceVault.ImportPlaintext(sourcePairs).IsSuccess);

            var destinationVault = new LegacyStoredCredentialVault(destinationDirectory);
            Assert.True(destinationVault.CreateEncrypted(Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))).IsSuccess);
            string destinationPairs = Path.Combine(destinationDirectory, "destination.txt");
            File.WriteAllText(destinationPairs, "destination-account:destination-password");
            Assert.True(destinationVault.ImportPlaintext(destinationPairs).IsSuccess);

            CredentialVaultWriteResult imported = destinationVault.ImportEncrypted(
                Path.Combine(sourceDirectory, "credentials.secure.json"),
                sourceKey);
            CredentialVaultLoadResult reopened = destinationVault.Load();

            Assert.True(imported.IsSuccess);
            Assert.True(reopened.Encrypted);
            Assert.Equal(2, reopened.Credentials.Count);
            Assert.Contains(reopened.Credentials, item => item.Username == "source-account");
            Assert.Contains(reopened.Credentials, item => item.Username == "destination-account");
        }
        finally
        {
            Directory.Delete(sourceDirectory, true);
            Directory.Delete(destinationDirectory, true);
        }
    }

    [Fact]
    public void DeleteVault_RemovesCredentialsButLeavesAccountFilesUntouched()
    {
        if (!OperatingSystem.IsWindows()) return;
        string directory = CreateDirectory();
        try
        {
            string accountPath = Path.Combine(directory, "account.maFile");
            File.WriteAllText(accountPath, "account-data");
            var vault = new LegacyStoredCredentialVault(directory);
            Assert.True(vault.CreateEncrypted(Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))).IsSuccess);

            CredentialVaultWriteResult deleted = vault.DeleteVault();

            Assert.True(deleted.IsSuccess);
            Assert.Equal(CredentialVaultState.Missing, vault.Load().State);
            Assert.Equal("account-data", File.ReadAllText(accountPath));
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    private static string CreateDirectory()
    {
        string path = Path.Combine(Path.GetTempPath(), "sdapp-credentials-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
