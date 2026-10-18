using SDAPlusPlus.Core.Cloud;
using SDAPlusPlus.Core.Vault;

namespace SDAPlusPlus.Core.Tests;

public sealed class VaultImportServiceTests : IDisposable
{
    private const string OldAccount = "{\"shared_secret\":\"MDEyMzQ1Njc4OWFiY2RlZg==\",\"account_name\":\"old-account\"}";
    private const string NewAccount = "{\"shared_secret\":\"MDEyMzQ1Njc4OWFiY2RlZg==\",\"account_name\":\"new-account\"}";
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "sdapp-import-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void CommitBacksUpExistingVaultAndWritesManifestLastPackage()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(Path.Combine(_directory, "old.maFile"), OldAccount);
        File.WriteAllText(Path.Combine(_directory, "manifest.json"), Manifest("old.maFile"));
        var package = new CloudPullPackage(
            Manifest("new.maFile"),
            new Dictionary<string, string> { ["new.maFile"] = NewAccount },
            IsEncrypted: false);
        var service = new VaultImportService(new LegacyVaultReader());

        VaultImportResult result = service.Commit(package, _directory);

        Assert.True(result.Success);
        Assert.Equal(NewAccount, File.ReadAllText(Path.Combine(_directory, "new.maFile")));
        Assert.NotNull(result.BackupDirectory);
        Assert.Equal(OldAccount, File.ReadAllText(Path.Combine(result.BackupDirectory!, "old.maFile")));
        VaultAccount imported = Assert.Single(new LegacyVaultReader().Load(_directory).Accounts);
        Assert.Equal("new-account", imported.Name);
    }

    [Fact]
    public void UnsafeManifestIsRejectedBeforeLocalVaultChanges()
    {
        Directory.CreateDirectory(_directory);
        string localManifest = Manifest("old.maFile");
        File.WriteAllText(Path.Combine(_directory, "old.maFile"), OldAccount);
        File.WriteAllText(Path.Combine(_directory, "manifest.json"), localManifest);
        var package = new CloudPullPackage(
            Manifest("../outside.maFile"),
            new Dictionary<string, string> { ["outside.maFile"] = NewAccount },
            IsEncrypted: false);

        VaultImportResult result = new VaultImportService(new LegacyVaultReader()).Commit(package, _directory);

        Assert.False(result.Success);
        Assert.Equal(localManifest, File.ReadAllText(Path.Combine(_directory, "manifest.json")));
        Assert.False(File.Exists(Path.Combine(_directory, "outside.maFile")));
    }

    [Fact]
    public void InvalidEncryptedCipherTextIsRejectedBeforeLocalVaultChanges()
    {
        Directory.CreateDirectory(_directory);
        string localManifest = Manifest("old.maFile");
        File.WriteAllText(Path.Combine(_directory, "old.maFile"), OldAccount);
        File.WriteAllText(Path.Combine(_directory, "manifest.json"), localManifest);
        const string encryptedManifest = """
            {"encrypted":true,"entries":[{"filename":"bad.maFile","steamid":0,"favorite":false,"encryption_iv":"MDEyMzQ1Njc4OWFiY2RlZg==","encryption_salt":"c2FsdA=="}]}
            """;
        var package = new CloudPullPackage(
            encryptedManifest,
            new Dictionary<string, string> { ["bad.maFile"] = "not-base64" },
            IsEncrypted: true);

        VaultImportResult result = new VaultImportService(new LegacyVaultReader()).Commit(package, _directory);

        Assert.False(result.Success);
        Assert.Equal(localManifest, File.ReadAllText(Path.Combine(_directory, "manifest.json")));
        Assert.False(File.Exists(Path.Combine(_directory, "bad.maFile")));
    }

    private static string Manifest(string fileName) => $$"""
        {"encrypted":false,"entries":[{"filename":"{{fileName}}","steamid":0,"favorite":false}]}
        """;

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }
}
