using SDAPlusPlus.Core.Vault;

namespace SDAPlusPlus.Core.Tests;

public sealed class LegacyVaultAccountWriterTests : IDisposable
{
    private const string AccountJson = "{\"shared_secret\":\"MDEyMzQ1Njc4OWFiY2RlZg==\",\"account_name\":\"test-account\",\"Session\":{\"SteamID\":76561198000000000,\"AccessToken\":\"old-token\"}}";
    private const string CipherText = "sog5dFkWQMLDTt2hfG5Vkn6KV2Pp1rcleC5Al6JdAbZSh+JJcvS70yh3RUSqa/3Hy58ZVf0mhyWnZmOxfn9SDqjNuDy6M0hHjoeNsCv+OyG7uiEWAlFbdpxz/7DvvAhup/RLM16hDiYMlmhGhTja0D8de1O5/5ZaKAYXz4RqwmX1hsjrmKshT4m35bL+1bf4";
    private readonly string _sandbox;
    private readonly string _root;
    private readonly LegacyVaultReader _reader = new();

    public LegacyVaultAccountWriterTests()
    {
        _sandbox = Path.Combine(Path.GetTempPath(), "sdapp-writer-tests", Guid.NewGuid().ToString("N"));
        _root = Path.Combine(_sandbox, "maFiles");
    }

    [Fact]
    public void SavesUnencryptedSessionAndCreatesBackup()
    {
        CreateVault(encrypted: false, AccountJson);
        VaultAccount account = Assert.Single(_reader.Load(_root).Accounts);
        account.GuardAccount.Session!.AccessToken = "new-token";

        VaultWriteResult result = CreateWriter().SaveSession(_root, account);

        Assert.Equal(VaultWriteState.Saved, result.State);
        Assert.NotNull(result.BackupDirectory);
        Assert.True(File.Exists(Path.Combine(result.BackupDirectory!, "manifest.json")));
        Assert.True(File.Exists(Path.Combine(result.BackupDirectory!, "account.maFile")));
        Assert.Equal("new-token", Assert.Single(_reader.Load(_root).Accounts).GuardAccount.Session!.AccessToken);
    }

    [Fact]
    public void ReencryptsUpdatedSessionWithFreshLegacyMetadata()
    {
        CreateVault(encrypted: true, CipherText);
        VaultAccount account = Assert.Single(_reader.Load(_root, "correct horse").Accounts);
        account.GuardAccount.Session!.AccessToken = "encrypted-new-token";
        string manifestBefore = File.ReadAllText(Path.Combine(_root, "manifest.json"));
        string cipherBefore = File.ReadAllText(Path.Combine(_root, "account.maFile"));

        VaultWriteResult result = CreateWriter().SaveSession(_root, account, "correct horse");

        Assert.Equal(VaultWriteState.Saved, result.State);
        Assert.NotEqual(manifestBefore, File.ReadAllText(Path.Combine(_root, "manifest.json")));
        Assert.NotEqual(cipherBefore, File.ReadAllText(Path.Combine(_root, "account.maFile")));
        VaultAccount persisted = Assert.Single(_reader.Load(_root, "correct horse").Accounts);
        Assert.Equal("encrypted-new-token", persisted.GuardAccount.Session!.AccessToken);
        Assert.Equal(VaultLoadState.InvalidPassword, _reader.Load(_root, "wrong password").State);
    }

    [Fact]
    public void WrongPasswordDoesNotChangeEitherVaultFile()
    {
        CreateVault(encrypted: true, CipherText);
        VaultAccount account = Assert.Single(_reader.Load(_root, "correct horse").Accounts);
        account.GuardAccount.Session!.AccessToken = "must-not-be-written";
        string manifestBefore = File.ReadAllText(Path.Combine(_root, "manifest.json"));
        string cipherBefore = File.ReadAllText(Path.Combine(_root, "account.maFile"));

        VaultWriteResult result = CreateWriter().SaveSession(_root, account, "wrong password");

        Assert.Equal(VaultWriteState.InvalidPassword, result.State);
        Assert.Equal(manifestBefore, File.ReadAllText(Path.Combine(_root, "manifest.json")));
        Assert.Equal(cipherBefore, File.ReadAllText(Path.Combine(_root, "account.maFile")));
        Assert.False(Directory.Exists(Path.Combine(_sandbox, "SDAPlusPlus-backups")));
    }

    [Fact]
    public void UpdatesFavoriteWithoutRewritingAccountSecretFile()
    {
        CreateVault(encrypted: false, AccountJson);
        VaultAccount account = Assert.Single(_reader.Load(_root).Accounts);
        string accountBefore = File.ReadAllText(Path.Combine(_root, "account.maFile"));

        VaultWriteResult result = CreateWriter().SetFavorite(_root, account, favorite: false);

        Assert.Equal(VaultWriteState.Saved, result.State);
        Assert.Equal(accountBefore, File.ReadAllText(Path.Combine(_root, "account.maFile")));
        Assert.False(Assert.Single(_reader.Load(_root).Accounts).IsFavorite);
        Assert.True(File.Exists(Path.Combine(result.BackupDirectory!, "manifest.json")));
    }

    private LegacyVaultAccountWriter CreateWriter() => new(_reader);

    private void CreateVault(bool encrypted, string contents)
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(Path.Combine(_root, "account.maFile"), contents);
        string manifest = $$"""
            {
              "encrypted": {{encrypted.ToString().ToLowerInvariant()}},
              "entries": [
                {
                  "encryption_iv": "AAECAwQFBgcICQoLDA0ODw==",
                  "encryption_salt": "AQIDBAUGBwg=",
                  "filename": "account.maFile",
                  "steamid": 76561198000000000,
                  "favorite": true
                }
              ]
            }
            """;
        File.WriteAllText(Path.Combine(_root, "manifest.json"), manifest);
    }

    public void Dispose()
    {
        if (Directory.Exists(_sandbox))
        {
            Directory.Delete(_sandbox, recursive: true);
        }
    }
}
