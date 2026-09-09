using System.Collections.Concurrent;
using System.Net;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
using SDAPlusPlus.Core.Vault;

namespace SDAPlusPlus.Core.Monitoring;

public sealed partial class SteamCommunityMonitoringService : IAccountMonitoringService
{
    private static readonly TimeSpan CacheLifetime = TimeSpan.FromMinutes(5);
    private readonly ConcurrentDictionary<string, AccountMonitoringSnapshot> _cache = new(StringComparer.Ordinal);

    public async Task<AccountMonitoringSnapshot> LoadSnapshotAsync(
        VaultAccount account,
        bool forceRefresh = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(account);
        MonitoredSessionHealth sessionHealth = GetSessionHealth(account);
        if (!ulong.TryParse(account.SteamId, out ulong steamId) || steamId == 0)
        {
            return Empty(account, sessionHealth, "SteamID is missing");
        }

        if (!forceRefresh && _cache.TryGetValue(account.SteamId, out AccountMonitoringSnapshot? cached) &&
            DateTimeOffset.UtcNow - cached.LoadedAtUtc < CacheLifetime)
        {
            return cached with { Account = account, SessionHealth = sessionHealth, IsCached = true };
        }

        try
        {
            using HttpClient client = CreateClient(account);
            string html = await client.GetStringAsync(
                $"https://steamcommunity.com/profiles/{steamId}?l=english",
                cancellationToken).ConfigureAwait(false);
            bool isPrivate = html.Contains("profile_private_info", StringComparison.OrdinalIgnoreCase) ||
                html.Contains("This profile is private", StringComparison.OrdinalIgnoreCase);
            InventorySummary inventory = await LoadInventoryAsync(client, steamId, cancellationToken).ConfigureAwait(false);
            var snapshot = new AccountMonitoringSnapshot(
                account,
                sessionHealth,
                ParseBanStatus(html),
                ParseNumber(LevelRegex(), html),
                ParseNumber(GamesRegex(), html),
                inventory.State,
                inventory.TradableCount,
                isPrivate,
                DateTimeOffset.UtcNow,
                inventory.Error);
            _cache[account.SteamId] = snapshot;
            return snapshot;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or Newtonsoft.Json.JsonException)
        {
            return Empty(account, sessionHealth, exception.Message);
        }
    }

    private static async Task<InventorySummary> LoadInventoryAsync(
        HttpClient client,
        ulong steamId,
        CancellationToken cancellationToken)
    {
        int total = 0;
        string? startAssetId = null;
        do
        {
            string url = $"https://steamcommunity.com/inventory/{steamId}/730/2?l=english&count=2000";
            if (!string.IsNullOrWhiteSpace(startAssetId))
            {
                url += "&start_assetid=" + Uri.EscapeDataString(startAssetId);
            }

            using HttpResponseMessage response = await client.GetAsync(url, cancellationToken).ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.Forbidden)
            {
                return new InventorySummary(MonitoredInventoryState.Private, null);
            }
            if ((int)response.StatusCode == 429)
            {
                return new InventorySummary(MonitoredInventoryState.RateLimited, null);
            }
            if (!response.IsSuccessStatusCode)
            {
                return new InventorySummary(MonitoredInventoryState.Unavailable, null, $"HTTP {(int)response.StatusCode}");
            }

            JObject root = JObject.Parse(await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
            if (root.Value<int?>("success") != 1)
            {
                return new InventorySummary(MonitoredInventoryState.Unavailable, null, "Steam rejected the inventory request");
            }

            HashSet<string> tradableDescriptions = (root["descriptions"] as JArray ?? [])
                .OfType<JObject>()
                .Where(description => description.Value<int?>("tradable") == 1)
                .Select(description => DescriptionKey(description.Value<string>("classid"), description.Value<string>("instanceid")))
                .ToHashSet(StringComparer.Ordinal);
            total += (root["assets"] as JArray ?? [])
                .OfType<JObject>()
                .Where(asset => tradableDescriptions.Contains(DescriptionKey(asset.Value<string>("classid"), asset.Value<string>("instanceid"))))
                .Sum(asset => Math.Max(1, asset.Value<int?>("amount") ?? 1));
            startAssetId = root.Value<bool?>("more_items") == true ? root.Value<string>("last_assetid") : null;
        }
        while (!string.IsNullOrWhiteSpace(startAssetId));

        return new InventorySummary(MonitoredInventoryState.Available, total);
    }

    private static HttpClient CreateClient(VaultAccount account)
    {
        var handler = new HttpClientHandler
        {
            AutomaticDecompression = DecompressionMethods.All,
            CookieContainer = account.GuardAccount.Session?.GetCookies() ?? new CookieContainer()
        };
        var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(20) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("SDA++/1.7 AccountMonitor");
        return client;
    }

    private static AccountMonitoringSnapshot Empty(
        VaultAccount account,
        MonitoredSessionHealth sessionHealth,
        string error) =>
        new(account, sessionHealth, "Unknown", null, null, MonitoredInventoryState.Unavailable, null, false, DateTimeOffset.UtcNow, error);

    private static MonitoredSessionHealth GetSessionHealth(VaultAccount account)
    {
        SteamAuth.SessionData? session = account.GuardAccount.Session;
        if (session is null || string.IsNullOrWhiteSpace(session.RefreshToken)) return MonitoredSessionHealth.Missing;
        if (session.IsRefreshTokenExpired()) return MonitoredSessionHealth.RefreshExpired;
        if (session.IsAccessTokenExpired()) return MonitoredSessionHealth.AccessExpired;
        return MonitoredSessionHealth.Active;
    }

    private static string ParseBanStatus(string html)
    {
        Match match = BanBlockRegex().Match(html);
        if (!match.Success) return "Clear";
        string value = WebUtility.HtmlDecode(HtmlTagRegex().Replace(match.Groups["value"].Value, " ")).Trim();
        return string.IsNullOrWhiteSpace(value) ? "Clear" : WhitespaceRegex().Replace(value, " ");
    }

    private static int? ParseNumber(Regex regex, string html)
    {
        Match match = regex.Match(html);
        string value = match.Success ? Regex.Replace(match.Groups["value"].Value, "[^0-9]", string.Empty) : string.Empty;
        return int.TryParse(value, out int number) ? number : null;
    }

    private static string DescriptionKey(string? classId, string? instanceId) =>
        (classId ?? string.Empty) + ":" + (instanceId ?? "0");

    private sealed record InventorySummary(MonitoredInventoryState State, int? TradableCount, string? Error = null);

    [GeneratedRegex(@"friendPlayerLevelNum[^>]*>\s*(?<value>\d+)", RegexOptions.IgnoreCase)]
    private static partial Regex LevelRegex();

    [GeneratedRegex(@"profile_count_link_label[^>]*>\s*Games\s*</span>[\s\S]*?profile_count_link_total[^>]*>\s*(?<value>[\d,\s]+)", RegexOptions.IgnoreCase)]
    private static partial Regex GamesRegex();

    [GeneratedRegex(@"profile_ban_status[^>]*>(?<value>[\s\S]*?)</div>", RegexOptions.IgnoreCase)]
    private static partial Regex BanBlockRegex();

    [GeneratedRegex("<[^>]+>")]
    private static partial Regex HtmlTagRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();
}
