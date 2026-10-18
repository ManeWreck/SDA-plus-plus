using SDAPlusPlus.Core.Vault;

namespace SDAPlusPlus.Core.Tests;

public sealed class LegacyVaultAccountImportServiceTests : IDisposable
{
    private const string AccountJson = "{\"shared_secret\":\"MDEyMzQ1Njc4OWFiY2RlZg==\",\"account_name\":\"test-account\",\"Session\":{\"SteamID\":76561198000000000,\"AccessToken\":\"token\"}}";
    private const string CipherText = "sog5dFkWQMLDTt2hfG5Vkn6KV2Pp1rcleC5Al6JdAbZSh+JJcvS70yh3RUSqa/3Hy58ZVf0mhyWnZmOxfn9SDqjNuDy6M0hHjoeNsCv+OyG7uiEWAlFbdpxz/7DvvAhup/RLM16hDiYMlmhGhTja0D8de1O5/5ZaKAYXz4RqwmX1hsjrmKshT4m35bL+1bf4";
    private readonly string _sandbox = Path.Combine(Path.GetTempPath(), "sdapp-import-tests", Guid.NewGuid().ToString("N"));
    private readonly LegacyVaultReader _reader = new();

    [Fact]
    public void ImportsPlainAccountIntoNewVault()
    {
        string source = CreateSource(AccountJson, encrypted: false, includeManifest: false);
        string destination = Path.Combine(_sandbox, "destination");

        VaultAccountImportResult result = CreateService().Import(source, destination);

        Assert.Equal(VaultAccountImportState.Imported, result.State);
        VaultAccount account = Assert.Single(_reader.Load(destination).Accounts);
        Assert.Equal("test-account", account.Name);
        Assert.True(File.Exists(Path.Combine(destination, "manifest.json")));
    }

    [Fact]
    public void ImportsLegacyEncryptedSourceWithMatchingManifest()
    {
        string source = CreateSource(CipherText, encrypted: true, includeManifest: true);
        string destination = Path.Combine(_sandbox, "destination");

        VaultAccountImportResult result = CreateService().Import(source, destination, sourcePassword: "correct horse");

        Assert.Equal(VaultAccountImportState.Imported, result.State);
        Assert.Equal("test-account", Assert.Single(_reader.Load(destination).Accounts).Name);
    }

    [Fact]
    public void WrongDestinationPasswordLeavesEncryptedVaultUnchanged()
    {
        string source = CreateSource(AccountJson, encrypted: false, includeManifest: false);
        string destination = Path.Combine(_sandbox, "destination");
        Directory.CreateDirectory(destination);
        File.WriteAllText(Path.Combine(destination, "existing.maFile"), CipherText);
        WriteManifest(destination, encrypted: true, "existing.maFile");
        string manifestBefore = File.ReadAllText(Path.Combine(destination, "manifest.json"));
        string accountBefore = File.ReadAllText(Path.Combine(destination, "existing.maFile"));

        VaultAccountImportResult result = CreateService().Import(source, destination, destinationPassword: "wrong password");

        Assert.Equal(VaultAccountImportState.InvalidDestinationPassword, result.State);
        Assert.Equal(manifestBefore, File.ReadAllText(Path.Combine(destination, "manifest.json")));
        Assert.Equal(accountBefore, File.ReadAllText(Path.Combine(destination, "existing.maFile")));
        Assert.Equal(2, Directory.GetFiles(destination).Length);
    }

    [Fact]
    public void DuplicateImportNeverOverwritesExistingAccount()
    {
        string source = CreateSource(AccountJson, encrypted: false, includeManifest: false);
        string destination = Path.Combine(_sandbox, "destination");
        VaultAccountImportResult first = CreateService().Import(source, destination);
        string manifestBefore = File.ReadAllText(Path.Combine(destination, "manifest.json"));
        string existingPath = Directory.GetFiles(destination, "*.maFile").Single();
        string accountBefore = File.ReadAllText(existingPath);

        VaultAccountImportResult second = CreateService().Import(source, destination);

        Assert.True(first.IsSuccess);
        Assert.Equal(VaultAccountImportState.Duplicate, second.State);
        Assert.Equal(manifestBefore, File.ReadAllText(Path.Combine(destination, "manifest.json")));
        Assert.Equal(accountBefore, File.ReadAllText(existingPath));
        Assert.Single(Directory.GetFiles(destination, "*.maFile"));
    }

    private LegacyVaultAccountImportService CreateService() => new(_reader);

    [Fact]
    public void UnlistedAccountFileIsNeverDeletedOnCollision()
    {
        string source = CreateSource(AccountJson, false, false);
        string destination = Path.Combine(_sandbox, "destination");
        Directory.CreateDirectory(destination);
        string existing = Path.Combine(destination, "76561198000000000.maFile");
        File.WriteAllText(existing, "irreplaceable pending enrollment");

        CreateService().Import(source, destination);

        Assert.True(File.Exists(existing));
        Assert.Equal("irreplaceable pending enrollment", File.ReadAllText(existing));
    }

    [Fact]
    public void NewVaultWithPasswordPersistsEncryptedRecoveryData()
    {
        var guard = new SteamAuth.SteamGuardAccount
        {
            AccountName = "pending-test",
            SharedSecret = "MDEyMzQ1Njc4OWFiY2RlZg==",
            RevocationCode = "R12345",
            Session = new SteamAuth.SessionData { SteamID = 76561198000000000 }
        };
        var account = new VaultAccount(guard.AccountName, guard.Session.SteamID.ToString(), guard, false, false);
        string destination = Path.Combine(_sandbox, "encrypted-new");
        var result = CreateService().ImportAccount(account, destination, "long-test-password");

        Assert.True(result.IsSuccess);
        Assert.Equal(VaultLoadState.Locked, _reader.Load(destination).State);
        string stored = File.ReadAllText(Directory.GetFiles(destination, "*.maFile").Single());
        Assert.DoesNotContain(guard.SharedSecret, stored);
        Assert.DoesNotContain(guard.RevocationCode, stored);
        var restored = Assert.Single(_reader.Load(destination, "long-test-password").Accounts);
        Assert.Equal(guard.RevocationCode, restored.GuardAccount.RevocationCode);
        Assert.False(restored.GuardAccount.FullyEnrolled);
    }

    [Fact]
    public void PartialDestinationIsNotModified()
    {
        string source = CreateSource(AccountJson, false, false);
        string destination = Path.Combine(_sandbox, "destination");
        Directory.CreateDirectory(destination);
        WriteManifest(destination, false, "missing.maFile");
        string before = File.ReadAllText(Path.Combine(destination, "manifest.json"));

        var result = CreateService().Import(source, destination);

        Assert.Equal(VaultAccountImportState.DestinationCorrupt, result.State);
        Assert.Equal(before, File.ReadAllText(Path.Combine(destination, "manifest.json")));
        Assert.Empty(Directory.GetFiles(destination, "*.maFile"));
    }

    private string CreateSource(string contents, bool encrypted, bool includeManifest)
    {
        string directory = Path.Combine(_sandbox, "source-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "account.maFile");
        File.WriteAllText(path, contents);
        if (includeManifest)
        {
            WriteManifest(directory, encrypted, "account.maFile");
        }

        return path;
    }

    private static void WriteManifest(string directory, bool encrypted, string filename)
    {
        string manifest = $$"""
            {
              "encrypted": {{encrypted.ToString().ToLowerInvariant()}},
              "entries": [{
                "encryption_iv": "AAECAwQFBgcICQoLDA0ODw==",
                "encryption_salt": "AQIDBAUGBwg=",
                "filename": "{{filename}}",
                "steamid": 76561198000000000,
                "favorite": false
              }]
            }
            """;
        File.WriteAllText(Path.Combine(directory, "manifest.json"), manifest);
    }

    public void Dispose()
    {
        if (Directory.Exists(_sandbox))
        {
            Directory.Delete(_sandbox, recursive: true);
        }
    }
}
