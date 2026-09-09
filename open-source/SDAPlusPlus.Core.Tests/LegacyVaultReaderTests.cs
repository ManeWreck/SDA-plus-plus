using SDAPlusPlus.Core.Vault;

namespace SDAPlusPlus.Core.Tests;

public sealed class LegacyVaultReaderTests : IDisposable
{
    private const string AccountJson = "{\"shared_secret\":\"MDEyMzQ1Njc4OWFiY2RlZg==\",\"account_name\":\"test-account\",\"Session\":{\"SteamID\":76561198000000000,\"AccessToken\":\"token\"}}";
    private const string CipherText = "sog5dFkWQMLDTt2hfG5Vkn6KV2Pp1rcleC5Al6JdAbZSh+JJcvS70yh3RUSqa/3Hy58ZVf0mhyWnZmOxfn9SDqjNuDy6M0hHjoeNsCv+OyG7uiEWAlFbdpxz/7DvvAhup/RLM16hDiYMlmhGhTja0D8de1O5/5ZaKAYXz4RqwmX1hsjrmKshT4m35bL+1bf4";
    private readonly string _sandbox;
    private readonly string _root;
    private readonly LegacyVaultReader _reader = new();

    public LegacyVaultReaderTests()
    {
        _sandbox = Path.Combine(Path.GetTempPath(), "sdapp-core-tests", Guid.NewGuid().ToString("N"));
        _root = Path.Combine(_sandbox, "vault");
    }

    [Fact]
    public void LoadsUnencryptedManifest()
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(Path.Combine(_root, "account.maFile"), AccountJson);
        WriteManifest(encrypted: false, filename: "account.maFile");

        VaultLoadResult result = _reader.Load(_root);

        Assert.Equal(VaultLoadState.Ready, result.State);
        VaultAccount account = Assert.Single(result.Accounts);
        Assert.Equal("test-account", account.Name);
        Assert.Equal("76561198000000000", account.SteamId);
        Assert.True(account.HasMobileSession);
    }

    [Fact]
    public void UnlocksLegacyEncryptedManifestWithKnownVector()
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(Path.Combine(_root, "account.maFile"), CipherText);
        WriteManifest(encrypted: true, filename: "account.maFile");

        VaultLoadResult locked = _reader.Load(_root);
        VaultLoadResult unlocked = _reader.Load(_root, "correct horse");

        Assert.Equal(VaultLoadState.Locked, locked.State);
        Assert.Equal(VaultLoadState.Ready, unlocked.State);
        Assert.Equal("test-account", Assert.Single(unlocked.Accounts).Name);
    }

    [Fact]
    public void RejectsWrongPasswordWithoutReturningAccounts()
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(Path.Combine(_root, "account.maFile"), CipherText);
        WriteManifest(encrypted: true, filename: "account.maFile");

        VaultLoadResult result = _reader.Load(_root, "wrong password");

        Assert.Equal(VaultLoadState.InvalidPassword, result.State);
        Assert.Empty(result.Accounts);
    }

    [Fact]
    public void RejectsManifestEntryOutsideVaultDirectory()
    {
        Directory.CreateDirectory(_root);
        string outside = Path.Combine(Directory.GetParent(_root)!.FullName, "outside.maFile");
        File.WriteAllText(outside, AccountJson);
        WriteManifest(encrypted: false, filename: "../outside.maFile");

        VaultLoadResult result = _reader.Load(_root);

        Assert.Equal(VaultLoadState.Partial, result.State);
        Assert.Empty(result.Accounts);
        Assert.Equal(1, result.SkippedFiles);
    }

    private void WriteManifest(bool encrypted, string filename)
    {
        string manifest = $$"""
            {
              "encrypted": {{encrypted.ToString().ToLowerInvariant()}},
              "entries": [
                {
                  "encryption_iv": "AAECAwQFBgcICQoLDA0ODw==",
                  "encryption_salt": "AQIDBAUGBwg=",
                  "filename": "{{filename}}",
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
