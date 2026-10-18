using Newtonsoft.Json.Linq;
using SDAPlusPlus.Core.Vault;

namespace SDAPlusPlus.Core.Tests;

public sealed class LegacyVaultEncryptionServiceTests : IDisposable
{
    private const string AccountJson = "{\"shared_secret\":\"MDEyMzQ1Njc4OWFiY2RlZg==\",\"account_name\":\"test-account\",\"Session\":{\"SteamID\":76561198000000000}}";
    private readonly string _sandbox;
    private readonly string _root;
    private readonly LegacyVaultReader _reader = new();

    public LegacyVaultEncryptionServiceTests()
    {
        _sandbox = Path.Combine(Path.GetTempPath(), "sdapp-encryption-tests", Guid.NewGuid().ToString("N"));
        _root = Path.Combine(_sandbox, "maFiles");
    }

    [Fact]
    public void EnablesProtectionAndPreservesUnknownManifestFields()
    {
        CreatePlainVault();

        VaultEncryptionResult result = CreateService().ChangeProtection(_root, null, "new password");

        Assert.True(result.Success);
        Assert.True(result.IsEncrypted);
        Assert.NotNull(result.BackupDirectory);
        Assert.True(File.Exists(Path.Combine(result.BackupDirectory!, "manifest.json")));
        Assert.Equal(VaultLoadState.Locked, _reader.Load(_root).State);
        Assert.Single(_reader.Load(_root, "new password").Accounts);
        JObject manifest = JObject.Parse(File.ReadAllText(Path.Combine(_root, "manifest.json")));
        Assert.Equal("preserve-me", manifest.Value<string>("sdapp_custom"));
        Assert.Equal("entry-extra", manifest["entries"]![0]!.Value<string>("custom_entry"));
    }

    [Fact]
    public void ChangesPasswordThenDisablesProtection()
    {
        CreatePlainVault();
        Assert.True(CreateService().ChangeProtection(_root, null, "first password").Success);

        VaultEncryptionResult changed = CreateService().ChangeProtection(_root, "first password", "second password");

        Assert.True(changed.Success);
        Assert.Equal(VaultLoadState.InvalidPassword, _reader.Load(_root, "first password").State);
        Assert.Single(_reader.Load(_root, "second password").Accounts);

        VaultEncryptionResult disabled = CreateService().ChangeProtection(_root, "second password", null);

        Assert.True(disabled.Success);
        Assert.False(disabled.IsEncrypted);
        Assert.Single(_reader.Load(_root).Accounts);
        Assert.Contains("test-account", File.ReadAllText(Path.Combine(_root, "account.maFile")));
    }

    [Fact]
    public void WrongPasswordLeavesAllOriginalBytesUntouched()
    {
        CreatePlainVault();
        Assert.True(CreateService().ChangeProtection(_root, null, "correct password").Success);
        string manifestBefore = File.ReadAllText(Path.Combine(_root, "manifest.json"));
        string accountBefore = File.ReadAllText(Path.Combine(_root, "account.maFile"));

        VaultEncryptionResult result = CreateService().ChangeProtection(_root, "wrong password", "another password");

        Assert.Equal(VaultEncryptionState.InvalidPassword, result.State);
        Assert.Equal(manifestBefore, File.ReadAllText(Path.Combine(_root, "manifest.json")));
        Assert.Equal(accountBefore, File.ReadAllText(Path.Combine(_root, "account.maFile")));
    }

    [Fact]
    public void RejectsEntryThatEscapesVaultDirectory()
    {
        CreatePlainVault(filename: "..\\outside.maFile");
        File.WriteAllText(Path.Combine(_sandbox, "outside.maFile"), AccountJson);

        VaultEncryptionResult result = CreateService().ChangeProtection(_root, null, "new password");

        Assert.Equal(VaultEncryptionState.Corrupt, result.State);
        Assert.Equal(AccountJson, File.ReadAllText(Path.Combine(_sandbox, "outside.maFile")));
    }

    private LegacyVaultEncryptionService CreateService() => new(_reader);

    private void CreatePlainVault(string filename = "account.maFile")
    {
        Directory.CreateDirectory(_root);
        if (!filename.Contains("..", StringComparison.Ordinal))
        {
            File.WriteAllText(Path.Combine(_root, filename), AccountJson);
        }

        File.WriteAllText(Path.Combine(_root, "manifest.json"), $$"""
            {
              "encrypted": false,
              "sdapp_custom": "preserve-me",
              "entries": [
                {
                  "encryption_iv": null,
                  "encryption_salt": null,
                  "filename": "{{filename.Replace("\\", "\\\\")}}",
                  "steamid": 76561198000000000,
                  "favorite": true,
                  "custom_entry": "entry-extra"
                }
              ]
            }
            """);
    }

    public void Dispose()
    {
        if (Directory.Exists(_sandbox))
        {
            Directory.Delete(_sandbox, recursive: true);
        }
    }
}
