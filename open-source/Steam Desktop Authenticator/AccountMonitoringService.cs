using Newtonsoft.Json.Linq;
using SteamAuth;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Steam_Desktop_Authenticator
{
    internal sealed class AccountMonitoringService
    {
        private static readonly Regex LevelRegex = new Regex(@"friendPlayerLevelNum[^>]*>\s*(?<value>\d+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex GamesRegex = new Regex(@"profile_count_link_label[^>]*>\s*Games\s*</span>[\s\S]*?profile_count_link_total[^>]*>\s*(?<value>[\d,\s]+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex BanBlockRegex = new Regex(@"profile_ban_status[^>]*>(?<value>[\s\S]*?)</div>", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly ConcurrentDictionary<ulong, CachedSnapshot> SnapshotCache = new ConcurrentDictionary<ulong, CachedSnapshot>();
        private static readonly TimeSpan CacheLifetime = TimeSpan.FromMinutes(5);

        public async Task<AccountSnapshot> LoadSnapshotAsync(SteamGuardAccount account, bool forceRefresh = false)
        {
            AccountSnapshot snapshot = new AccountSnapshot
            {
                Account = account,
                AccountName = account?.AccountName ?? string.Empty,
                SteamId = account?.Session?.SteamID ?? 0,
                VacStatus = Localizer.Choose("Unknown", "Неизвестно"),
                Level = Localizer.Choose("Private", "Скрыт"),
                Games = Localizer.Choose("Private", "Скрыты"),
                Inventory = Localizer.Choose("Unavailable", "Недоступен")
            };

            snapshot.SessionHealth = GetSessionHealth(account);

            if (snapshot.SteamId == 0)
            {
                snapshot.Error = Localizer.Choose("SteamID is missing", "SteamID отсутствует");
                return snapshot;
            }

            if (!forceRefresh
                && SnapshotCache.TryGetValue(snapshot.SteamId, out CachedSnapshot cached)
                && DateTimeOffset.UtcNow - cached.StoredUtc < CacheLifetime)
            {
                cached.Snapshot.Account = account;
                cached.Snapshot.IsCached = true;
                cached.Snapshot.SessionHealth = snapshot.SessionHealth;
                return cached.Snapshot;
            }

            try
            {
                using HttpClient client = CreateClient(account);
                string profileUrl = $"https://steamcommunity.com/profiles/{snapshot.SteamId}?l=english";
                string html = await client.GetStringAsync(profileUrl);
                snapshot.IsPrivateProfile = html.IndexOf("profile_private_info", StringComparison.OrdinalIgnoreCase) >= 0
                    || html.IndexOf("This profile is private", StringComparison.OrdinalIgnoreCase) >= 0;
                snapshot.VacStatus = ParseBanStatus(html);
                snapshot.Level = ParseNumber(LevelRegex, html, Localizer.Choose("Private", "Скрыт"));
                snapshot.Games = ParseNumber(GamesRegex, html, Localizer.Choose("Private", "Скрыты"));

                InventoryResult inventory = await LoadInventoryAsync(account);
                snapshot.Inventory = inventory.Success
                    ? inventory.TotalCount.ToString()
                    : inventory.Error;
                snapshot.Loaded = true;
                snapshot.LoadedAtUtc = DateTimeOffset.UtcNow;
                SnapshotCache[snapshot.SteamId] = new CachedSnapshot(snapshot, snapshot.LoadedAtUtc);
            }
            catch (Exception ex)
            {
                snapshot.Error = ex.Message;
            }

            return snapshot;
        }

        public async Task<InventoryResult> LoadInventoryAsync(SteamGuardAccount account, int count = 2000)
        {
            if (account?.Session?.SteamID == 0)
            {
                return InventoryResult.Failed(Localizer.Choose("SteamID is missing", "SteamID отсутствует"));
            }

            try
            {
                using HttpClient client = CreateClient(account);
                List<InventoryItem> items = new List<InventoryItem>();
                string startAssetId = null;
                do
                {
                    string url = $"https://steamcommunity.com/inventory/{account.Session.SteamID}/730/2?l=english&count={Math.Max(1, Math.Min(count, 2000))}";
                    if (!string.IsNullOrWhiteSpace(startAssetId))
                    {
                        url += "&start_assetid=" + Uri.EscapeDataString(startAssetId);
                    }

                    using HttpResponseMessage response = await client.GetAsync(url);
                    if (response.StatusCode == HttpStatusCode.Forbidden)
                    {
                        return InventoryResult.Failed(Localizer.Choose("Private", "Приватный"));
                    }
                    if (response.StatusCode == (HttpStatusCode)429)
                    {
                        return InventoryResult.Failed(Localizer.Choose("Steam rate limit", "Лимит запросов Steam"));
                    }
                    if (!response.IsSuccessStatusCode)
                    {
                        return InventoryResult.Failed($"HTTP {(int)response.StatusCode}");
                    }

                    JObject root = JObject.Parse(await response.Content.ReadAsStringAsync());
                    if (root.Value<int?>("success") != 1)
                    {
                        return InventoryResult.Failed(Localizer.Choose("Steam rejected the inventory request", "Steam отклонил запрос инвентаря"));
                    }

                    Dictionary<string, JObject> descriptions = (root["descriptions"] as JArray ?? new JArray())
                        .OfType<JObject>()
                        .GroupBy(description => DescriptionKey(description.Value<string>("classid"), description.Value<string>("instanceid")))
                        .ToDictionary(group => group.Key, group => group.First());

                    foreach (JObject asset in (root["assets"] as JArray ?? new JArray()).OfType<JObject>())
                    {
                        descriptions.TryGetValue(DescriptionKey(asset.Value<string>("classid"), asset.Value<string>("instanceid")), out JObject description);
                        if (description?.Value<int?>("tradable") != 1)
                        {
                            continue;
                        }

                        items.Add(new InventoryItem
                        {
                            Name = description.Value<string>("market_hash_name") ?? description.Value<string>("name") ?? asset.Value<string>("assetid"),
                            Amount = asset.Value<int?>("amount") ?? 1,
                            Tradable = true,
                            Marketable = description.Value<int?>("marketable") == 1
                        });
                    }

                    startAssetId = root.Value<bool?>("more_items") == true ? root.Value<string>("last_assetid") : null;
                }
                while (!string.IsNullOrWhiteSpace(startAssetId));

                return new InventoryResult
                {
                    Success = true,
                    TotalCount = items.Sum(item => item.Amount),
                    Items = items
                };
            }
            catch (Exception ex)
            {
                return InventoryResult.Failed(ex.Message);
            }
        }

        private static HttpClient CreateClient(SteamGuardAccount account)
        {
            HttpClientHandler handler = new HttpClientHandler
            {
                AutomaticDecompression = DecompressionMethods.All,
                CookieContainer = account?.Session?.GetCookies() ?? new CookieContainer()
            };
            HttpClient client = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(20)
            };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("SDA++/1.2 SteamAccountMonitor");
            return client;
        }

        private static string ParseBanStatus(string html)
        {
            Match match = BanBlockRegex.Match(html ?? string.Empty);
            if (!match.Success)
            {
                return Localizer.Choose("No visible bans", "Видимых банов нет");
            }

            string text = StripTags(match.Groups["value"].Value);
            if (text.IndexOf("VAC ban", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "VAC: " + text;
            }
            if (text.IndexOf("game ban", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return Localizer.Choose("Game ban: ", "Игровой бан: ") + text;
            }
            if (text.IndexOf("community ban", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return Localizer.Choose("Community ban", "Бан сообщества");
            }

            return string.IsNullOrWhiteSpace(text) ? Localizer.Choose("No visible bans", "Видимых банов нет") : text;
        }

        private static string ParseNumber(Regex regex, string html, string fallback)
        {
            Match match = regex.Match(html ?? string.Empty);
            return match.Success ? match.Groups["value"].Value.Trim().Replace(",", string.Empty).Replace(" ", string.Empty) : fallback;
        }

        private static string StripTags(string value)
        {
            return WebUtility.HtmlDecode(Regex.Replace(value ?? string.Empty, "<[^>]+>", " ")).Trim();
        }

        private static string DescriptionKey(string classId, string instanceId) => (classId ?? string.Empty) + ":" + (instanceId ?? "0");

        private static string GetSessionHealth(SteamGuardAccount account)
        {
            if (account?.Session == null)
            {
                return Localizer.Choose("No session", "Нет сессии");
            }
            if (account.Session.IsRefreshTokenExpired())
            {
                return Localizer.Choose("Refresh expired", "Refresh истёк");
            }
            if (account.Session.IsAccessTokenExpired())
            {
                return Localizer.Choose("Access expired", "Access истёк");
            }
            return Localizer.Choose("Active", "Активна");
        }

        internal sealed class AccountSnapshot
        {
            public SteamGuardAccount Account { get; set; }
            public string AccountName { get; set; }
            public ulong SteamId { get; set; }
            public string VacStatus { get; set; }
            public string Level { get; set; }
            public string Games { get; set; }
            public string Inventory { get; set; }
            public string SessionHealth { get; set; }
            public bool Loaded { get; set; }
            public bool IsCached { get; set; }
            public bool IsPrivateProfile { get; set; }
            public DateTimeOffset LoadedAtUtc { get; set; }
            public string Error { get; set; }
        }

        private sealed class CachedSnapshot
        {
            public CachedSnapshot(AccountSnapshot snapshot, DateTimeOffset storedUtc)
            {
                Snapshot = snapshot;
                StoredUtc = storedUtc;
            }

            public AccountSnapshot Snapshot { get; }
            public DateTimeOffset StoredUtc { get; }
        }

        internal sealed class InventoryResult
        {
            public bool Success { get; set; }
            public int TotalCount { get; set; }
            public IReadOnlyList<InventoryItem> Items { get; set; } = Array.Empty<InventoryItem>();
            public string Error { get; set; }

            public static InventoryResult Failed(string error) => new InventoryResult { Error = error };
        }

        internal sealed class InventoryItem
        {
            public string Name { get; set; }
            public int Amount { get; set; }
            public bool Tradable { get; set; }
            public bool Marketable { get; set; }
        }
    }
}
