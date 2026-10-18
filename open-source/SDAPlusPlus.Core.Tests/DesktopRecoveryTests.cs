using System.Net;
using SDAPlusPlus.Core.Cloud;
using SDAPlusPlus.Core.Navigation;
using SDAPlusPlus.Core.Vault;

namespace SDAPlusPlus.Core.Tests;

public sealed class DesktopRecoveryTests
{
    [Fact]
    public void NavigationRoundTripAndNewBranch()
    {
        var history = new NavigationHistory<string>("dashboard");
        history.Visit("cloud"); history.Visit("credentials"); history.Visit("credentials");
        Assert.Equal("cloud", history.Back());
        Assert.Equal("dashboard", history.Back());
        Assert.False(history.CanGoBack);
        Assert.Equal("cloud", history.Forward());
        history.Visit("settings");
        Assert.False(history.CanGoForward);
        Assert.Equal("cloud", history.Back());
        Assert.Equal("settings", history.Forward());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("<html>sign in</html>")]
    [InlineData("{\"encrypted\":false,\"payload\":\"secret\"}")]
    [InlineData("{\"encrypted\":\"true\",\"payload\":\"secret\"}")]
    [InlineData("{\"encrypted\":true,\"encrypted\":false,\"payload\":\"secret\"}")]
    public void DownloadRejectsAbsentPlaintextOrMalformedDocuments(string? source)
    {
        var result = CloudCredentialDocument.ValidateDownload(source);
        Assert.False(result.Success);
        Assert.Null(result.Contents);
    }

    [Fact]
    public async Task CloudCredentialsUseReadOnlyGetWithoutManifestOrUpload()
    {
        const string encrypted = "{\"version\":3,\"encrypted\":true,\"payload\":\"synthetic-ciphertext\"}";
        using var handler = new DownloadHandler(HttpStatusCode.OK, encrypted);
        using var http = new HttpClient(handler);
        var router = new CloudVaultProviderRouter(http, Path.GetTempPath());
        var result = await router.DownloadFileAsync(new("https://example.test/dav", "test", "password", "SDA Vault"), "credentials.secure.json");
        Assert.True(result.Success);
        Assert.Equal(encrypted, result.Contents);
        Assert.Equal("GET https://example.test/dav/SDA%20Vault/credentials.secure.json", Assert.Single(handler.Requests));
    }

    [Theory]
    [InlineData(401)]
    [InlineData(404)]
    [InlineData(500)]
    public async Task CloudFailureCannotProduceAnImportableFile(int status)
    {
        using var handler = new DownloadHandler((HttpStatusCode)status, "failure");
        using var http = new HttpClient(handler);
        var router = new CloudVaultProviderRouter(http, Path.GetTempPath());
        var result = await router.DownloadFileAsync(new("https://example.test/dav", "test", "password", "vault"), "credentials.secure.json");
        Assert.False(result.Success);
        Assert.Null(result.Contents);
    }

    [Fact]
    public async Task UnsupportedFilenameDoesNotReachNetwork()
    {
        using var handler = new DownloadHandler(HttpStatusCode.OK, "{}");
        using var http = new HttpClient(handler);
        var router = new CloudVaultProviderRouter(http, Path.GetTempPath());
        Assert.False((await router.DownloadFileAsync(new("https://example.test", "", "", ""), "../secret")).Success);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public void LocalFolderImportsAccountsWithoutCopyingMachineSecrets()
    {
        string root = Path.Combine(Path.GetTempPath(), "sda-folder-test-" + Guid.NewGuid().ToString("N"));
        string source = Path.Combine(root, "source", "maFiles");
        string destination = Path.Combine(root, "destination");
        Directory.CreateDirectory(source);
        try
        {
            const string account = "{\"shared_secret\":\"MDEyMzQ1Njc4OWFiY2RlZg==\",\"account_name\":\"test-account\"}";
            File.WriteAllText(Path.Combine(source, "test.maFile"), account);
            File.WriteAllText(Path.Combine(source, "manifest.json"), "{\"encrypted\":false,\"entries\":[{\"filename\":\"test.maFile\"}]}");
            File.WriteAllText(Path.Combine(source, "webdav.secret.json"), "synthetic-machine-secret");
            var prepared = LocalVaultFolderImport.Prepare(Path.Combine(root, "source"));
            Assert.True(prepared.Success, prepared.Message);
            var imported = new VaultImportService(new LegacyVaultReader()).Commit(prepared.Package!, destination);
            Assert.True(imported.Success, imported.Message);
            Assert.Single(new LegacyVaultReader().Load(destination).Accounts);
            Assert.Equal(account, File.ReadAllText(Path.Combine(source, "test.maFile")));
            Assert.False(File.Exists(Path.Combine(destination, "webdav.secret.json")));
        }
        finally { Directory.Delete(root, true); }
    }

    [Theory]
    [InlineData("../outside.maFile")]
    [InlineData("x:outside.maFile")]
    [InlineData("nested/outside.maFile")]
    public void LocalFolderRejectsUnsafeManifestNames(string name)
    {
        string root = Path.Combine(Path.GetTempPath(), "sda-folder-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            File.WriteAllText(Path.Combine(root, "manifest.json"), "{\"entries\":[{\"filename\":\"" + name + "\"}]}");
            Assert.False(LocalVaultFolderImport.Prepare(root).Success);
        }
        finally { Directory.Delete(root, true); }
    }

    private sealed class DownloadHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        public List<string> Requests { get; } = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Requests.Add(request.Method + " " + request.RequestUri!.AbsoluteUri);
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body) });
        }
    }
}
