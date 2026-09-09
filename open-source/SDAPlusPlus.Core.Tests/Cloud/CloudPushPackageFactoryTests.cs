using Newtonsoft.Json.Linq;
using SDAPlusPlus.Core.Cloud;

namespace SDAPlusPlus.Core.Tests.Cloud;

public sealed class CloudPushPackageFactoryTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "sdapp-push-tests-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void Create_RejectsUnencryptedVault()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(Path.Combine(_directory, "manifest.json"), new JObject
        {
            ["encrypted"] = false,
            ["entries"] = new JArray()
        }.ToString());

        CloudPushPackageResult result = new CloudPushPackageFactory().Create(_directory);

        Assert.False(result.Success);
        Assert.Contains("encrypted", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Create_LoadsEncryptedAccountsAndManifest()
    {
        Directory.CreateDirectory(_directory);
        string filename = "76561198000000000.maFile";
        File.WriteAllText(Path.Combine(_directory, filename), Convert.ToBase64String(new byte[16]));
        File.WriteAllText(Path.Combine(_directory, "manifest.json"), new JObject
        {
            ["encrypted"] = true,
            ["entries"] = new JArray(new JObject
            {
                ["filename"] = filename,
                ["steamid"] = 76561198000000000UL,
                ["encryption_iv"] = Convert.ToBase64String(new byte[16]),
                ["encryption_salt"] = Convert.ToBase64String(new byte[8])
            })
        }.ToString());

        CloudPushPackageResult result = new CloudPushPackageFactory().Create(_directory);

        Assert.True(result.Success, result.Message);
        Assert.True(result.Package!.IsEncrypted);
        Assert.Contains(filename, result.Package.AccountFiles.Keys);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
    }
}
