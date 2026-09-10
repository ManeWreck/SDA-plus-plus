using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace SDAPlusPlus.Core.Updates;

public sealed record AvailableUpdate(string Version, string ReleaseUrl);

public sealed class GitHubUpdateChecker(HttpClient client)
{
    public const string Repository = "ManeWreck/SDA-plus-plus";

    public async Task<AvailableUpdate?> CheckAsync(string currentVersion, bool experimental, CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get,
            $"https://api.github.com/repos/{Repository}/releases?per_page=100");
        request.Headers.UserAgent.ParseAdd("SDAplusplus-Desktop-UpdateCheck/1.0");
        request.Headers.Accept.ParseAdd("application/vnd.github+json");
        using var response = await client.SendAsync(request, cancellationToken);
        if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests)
            throw new HttpRequestException("GitHub rate limit reached.");
        response.EnsureSuccessStatusCode();
        return SelectRelease(await response.Content.ReadAsStringAsync(cancellationToken), currentVersion, experimental);
    }

    public static AvailableUpdate? SelectRelease(string json, string currentVersion, bool experimental)
    {
        var current = ParseVersion(currentVersion) ?? throw new FormatException("Invalid application version.");
        using var document = JsonDocument.Parse(json);
        AvailableUpdate? selected = null;
        var best = current;
        foreach (var release in document.RootElement.EnumerateArray())
        {
            if (!release.TryGetProperty("draft", out var draft) || draft.ValueKind != JsonValueKind.False ||
                !release.TryGetProperty("prerelease", out var pre) || pre.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) continue;
            string? tag = release.TryGetProperty("tag_name", out var name) && name.ValueKind == JsonValueKind.String ? name.GetString() : null;
            var version = ParseVersion(tag);
            if (version is null || ((!experimental) && (pre.GetBoolean() || version.Suffix.Length > 0)) || Compare(version, best) <= 0) continue;
            // Only our packaged Windows x64 Desktop build, not source archives or legacy WinForms assets.
            if (version.Number <= new Version(1, 7, 0) || !release.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array) continue;
            string assetName = $"SDAplusplus-Desktop-{tag!.TrimStart('v')}-win-x64.zip";
            bool packaged = assets.EnumerateArray().Any(asset =>
                asset.TryGetProperty("name", out var assetTag) && assetTag.GetString() == assetName &&
                asset.TryGetProperty("state", out var state) && state.GetString() == "uploaded");
            if (!packaged) continue;
            best = version;
            selected = new AvailableUpdate(tag!, $"https://github.com/{Repository}/releases/tag/{Uri.EscapeDataString(tag!)}");
        }
        return selected;
    }

    private sealed record ReleaseVersion(Version Number, string Suffix);
    private static ReleaseVersion? ParseVersion(string? value)
    {
        var match = Regex.Match(value ?? "", @"^v?(\d+)\.(\d+)\.(\d+)(?:-([0-9A-Za-z.-]+))?(?:\+[0-9A-Za-z.-]+)?$");
        return match.Success && Version.TryParse($"{match.Groups[1]}.{match.Groups[2]}.{match.Groups[3]}", out var number)
            ? new ReleaseVersion(number, match.Groups[4].Value) : null;
    }

    private static int Compare(ReleaseVersion left, ReleaseVersion right)
    {
        int number = left.Number.CompareTo(right.Number);
        if (number != 0) return number;
        if (left.Suffix == right.Suffix) return 0;
        if (left.Suffix.Length == 0) return 1;
        if (right.Suffix.Length == 0) return -1;
        string[] a = left.Suffix.Split('.'), b = right.Suffix.Split('.');
        for (int i = 0; i < Math.Min(a.Length, b.Length); i++)
        {
            bool an = a[i].All(char.IsAsciiDigit), bn = b[i].All(char.IsAsciiDigit);
            int part = an && bn ? (a[i].Length == b[i].Length ? string.CompareOrdinal(a[i], b[i]) : a[i].Length.CompareTo(b[i].Length))
                : an != bn ? (an ? -1 : 1) : string.CompareOrdinal(a[i], b[i]);
            if (part != 0) return part;
        }
        return a.Length.CompareTo(b.Length);
    }
}
