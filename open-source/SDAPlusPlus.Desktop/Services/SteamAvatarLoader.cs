using System.Collections.Concurrent;
using System.Net;
using System.Text.RegularExpressions;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace SDAPlusPlus.Desktop.Services;

internal static class SteamAvatarLoader
{
    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromSeconds(10) };
    private static readonly ConcurrentDictionary<string, Lazy<Task<IImage?>>> SteamIdCache = new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<string, Lazy<Task<IImage?>>> UrlCache = new(StringComparer.Ordinal);

    public static Task<IImage?> LoadBySteamIdAsync(string steamId)
    {
        if (!ulong.TryParse(steamId, out ulong id) || id == 0) return Task.FromResult<IImage?>(null);
        return SteamIdCache.GetOrAdd(steamId, _ =>
            new Lazy<Task<IImage?>>(() => LoadProfileAsync(id), LazyThreadSafetyMode.ExecutionAndPublication)).Value;
    }

    public static Task<IImage?> LoadUrlAsync(string? value)
    {
        if (!TryValidateUrl(value, out Uri? uri)) return Task.FromResult<IImage?>(null);
        Uri validatedUri = uri!;
        return UrlCache.GetOrAdd(validatedUri.AbsoluteUri, _ =>
            new Lazy<Task<IImage?>>(() => DownloadAsync(validatedUri), LazyThreadSafetyMode.ExecutionAndPublication)).Value;
    }

    private static async Task<IImage?> LoadProfileAsync(ulong steamId)
    {
        try
        {
            string xml = await Client.GetStringAsync($"https://steamcommunity.com/profiles/{steamId}?xml=1").ConfigureAwait(false);
            Match match = Regex.Match(xml, "<avatarMedium><!\\[CDATA\\[(?<url>.*?)\\]\\]></avatarMedium>", RegexOptions.IgnoreCase);
            return match.Success ? await LoadUrlAsync(WebUtility.HtmlDecode(match.Groups["url"].Value)).ConfigureAwait(false) : null;
        }
        catch
        {
            return null;
        }
    }

    private static async Task<IImage?> DownloadAsync(Uri uri)
    {
        try
        {
            byte[] bytes = await Client.GetByteArrayAsync(uri).ConfigureAwait(false);
            using var stream = new MemoryStream(bytes, writable: false);
            return new Bitmap(stream);
        }
        catch
        {
            return null;
        }
    }

    private static bool TryValidateUrl(string? value, out Uri? uri)
    {
        uri = null;
        if (!Uri.TryCreate(value, UriKind.Absolute, out Uri? parsed) || parsed.Scheme != Uri.UriSchemeHttps || !IsSteamImageHost(parsed.Host))
        {
            return false;
        }
        uri = parsed;
        return true;
    }

    private static bool IsSteamImageHost(string host) =>
        host.EndsWith("steamstatic.com", StringComparison.OrdinalIgnoreCase) ||
        host.EndsWith("akamaihd.net", StringComparison.OrdinalIgnoreCase) ||
        host.EndsWith("steamusercontent.com", StringComparison.OrdinalIgnoreCase) ||
        host.EndsWith("steamcommunity.com", StringComparison.OrdinalIgnoreCase);
}
