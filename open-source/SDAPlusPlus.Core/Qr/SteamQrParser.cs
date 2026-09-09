using System.Globalization;

namespace SDAPlusPlus.Core.Qr;

public static class SteamQrParser
{
    public static bool TryParse(string? value, out SteamQrPayload? payload)
    {
        payload = null;
        if (!Uri.TryCreate(value?.Trim(), UriKind.Absolute, out Uri? uri) ||
            uri.Scheme != Uri.UriSchemeHttps ||
            !string.Equals(uri.Host, "s.team", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        string[] segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length != 3 ||
            !string.Equals(segments[0], "q", StringComparison.OrdinalIgnoreCase) ||
            !int.TryParse(segments[1], NumberStyles.None, CultureInfo.InvariantCulture, out int version) ||
            version is <= 0 or > ushort.MaxValue ||
            !ulong.TryParse(segments[2], NumberStyles.None, CultureInfo.InvariantCulture, out ulong clientId) ||
            clientId == 0)
        {
            return false;
        }

        payload = new SteamQrPayload(version, clientId);
        return true;
    }
}
