using System.Text.Json;
using SDAPlusPlus.Core.Updates;

namespace SDAPlusPlus.Core.Tests.Updates;

public sealed class GitHubUpdateCheckerTests
{
    private static object Release(string tag, bool pre = false, bool draft = false, bool packaged = true) => new
    {
        tag_name = tag, prerelease = pre, draft,
        html_url = "https://attacker.example",
        assets = packaged ? new[] { new { name = $"SDAplusplus-Desktop-{tag.TrimStart('v')}-win-x64.zip", state = "uploaded" } } : []
    };

    [Theory]
    [InlineData("1.7.1", "v1.7.2", false, false, true)]
    [InlineData("1.7.1", "v1.7.1", false, false, false)]
    [InlineData("1.7.2", "v1.7.1", false, false, false)]
    [InlineData("1.7.1", "v1.8.0-experimental", true, false, false)]
    [InlineData("1.7.1", "v1.8.0-experimental", false, false, false)]
    [InlineData("1.7.1-security-experimental", "v1.7.1", false, true, true)]
    [InlineData("1.7.1-security-experimental", "v1.7.2-security-experimental", true, true, true)]
    [InlineData("1.7.1-preview.9", "v1.7.1-preview.10", true, true, true)]
    [InlineData("1.7.1-preview.10", "v1.7.1-preview.9", true, true, false)]
    public void SelectsOnlyNewerAllowedVersions(string current, string tag, bool pre, bool experimental, bool expected)
    {
        var update = GitHubUpdateChecker.SelectRelease(JsonSerializer.Serialize(new[] { Release(tag, pre) }), current, experimental);
        Assert.Equal(expected, update is not null);
        if (update is not null) Assert.StartsWith("https://github.com/ManeWreck/SDA-plus-plus/releases/tag/", update.ReleaseUrl);
    }

    [Fact]
    public void IgnoresDraftsMissingPackagesAndLegacyBuilds()
    {
        string json = JsonSerializer.Serialize(new[] { Release("v2.0.0", draft: true), Release("v1.9.0", packaged: false), Release("v1.7.0") });
        Assert.Null(GitHubUpdateChecker.SelectRelease(json, "1.6.0", true));
    }

    [Fact]
    public void ChoosesHighestVersionNotApiOrder()
    {
        string json = JsonSerializer.Serialize(new[] { Release("v1.8.0"), Release("v1.10.0"), Release("v1.9.0") });
        Assert.Equal("v1.10.0", GitHubUpdateChecker.SelectRelease(json, "1.7.1", false)!.Version);
    }
}
