using System.Collections.Concurrent;
using System.Net;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
using SDAPlusPlus.Core.Vault;
using SteamAuth;

namespace SDAPlusPlus.Core.Confirmations;

public sealed class SteamConfirmationService : IConfirmationService
{
    private const int MaxConcurrentAccounts = 3;
    private const int MaxNetworkChecksPerPass = 5;
    private readonly ConcurrentDictionary<string, Lazy<Task<string?>>> _avatarCache = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, ConfirmationCacheEntry> _confirmationCache = new(StringComparer.Ordinal);

    public async Task<ConfirmationLoadResult> LoadAsync(
        IEnumerable<VaultAccount> accounts,
        CancellationToken cancellationToken = default,
        string? preferredSteamId = null,
        bool forceRefresh = false)
    {
        VaultAccount[] accountList = accounts?.Where(account => account is not null).ToArray() ?? [];
        DateTimeOffset now = DateTimeOffset.UtcNow;
        int refreshed = 0;
        using var gate = new SemaphoreSlim(MaxConcurrentAccounts);

        var due = new List<VaultAccount>();
        foreach (VaultAccount account in accountList)
        {
            string key = AccountKey(account);
            if (!forceRefresh && _confirmationCache.TryGetValue(key, out ConfirmationCacheEntry? cached) && IsFresh(cached, now))
            {
                continue;
            }

            if (account.GuardAccount.Session is null)
            {
                _confirmationCache[key] = ConfirmationCacheEntry.Expired(now, "no mobile web session");
                continue;
            }

            try
            {
                if (account.GuardAccount.Session.IsRefreshTokenExpired())
                {
                    _confirmationCache[key] = ConfirmationCacheEntry.Expired(now, "refresh token expired");
                    continue;
                }
            }
            catch
            {
                _confirmationCache[key] = ConfirmationCacheEntry.Failed(now, "invalid session token");
                continue;
            }

            due.Add(account);
        }

        VaultAccount[] batch = due
            .OrderByDescending(account => string.Equals(account.SteamId, preferredSteamId, StringComparison.Ordinal))
            .ThenBy(account => _confirmationCache.TryGetValue(AccountKey(account), out ConfirmationCacheEntry? entry)
                ? entry.CheckedAtUtc
                : DateTimeOffset.MinValue)
            .Take(MaxNetworkChecksPerPass)
            .ToArray();

        Task[] tasks = batch.Select(async account =>
        {
            await gate.WaitAsync(cancellationToken);
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                bool tokenRefreshed = false;
                if (account.GuardAccount.Session.IsAccessTokenExpired())
                {
                    await account.GuardAccount.Session.RefreshAccessToken();
                    tokenRefreshed = true;
                    Interlocked.Increment(ref refreshed);
                }

                Confirmation[] confirmations;
                try
                {
                    confirmations = await account.GuardAccount.FetchConfirmationsAsync() ?? [];
                }
                catch (Exception exception) when (!tokenRefreshed && IsAuthenticationFailure(exception))
                {
                    await account.GuardAccount.Session.RefreshAccessToken();
                    Interlocked.Increment(ref refreshed);
                    confirmations = await account.GuardAccount.FetchConfirmationsAsync() ?? [];
                }

                string? avatarUrl = confirmations.Length > 0
                    ? await LoadAccountAvatarAsync(account, cancellationToken)
                    : null;
                _confirmationCache[AccountKey(account)] = ConfirmationCacheEntry.Success(
                    DateTimeOffset.UtcNow,
                    confirmations,
                    avatarUrl);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                _confirmationCache[AccountKey(account)] = ConfirmationCacheEntry.Failed(
                    DateTimeOffset.UtcNow,
                    ClassifyFailure(exception));
            }
            finally
            {
                gate.Release();
            }
        }).ToArray();

        await Task.WhenAll(tasks);
        var items = new List<AccountConfirmation>();
        var failures = new List<string>();
        int expired = 0;
        int failed = 0;
        int checkedAccounts = 0;
        foreach (VaultAccount account in accountList)
        {
            if (!_confirmationCache.TryGetValue(AccountKey(account), out ConfirmationCacheEntry? entry)) continue;
            checkedAccounts++;
            if (entry.State == ConfirmationCacheState.Expired) expired++;
            if (entry.State == ConfirmationCacheState.Failed) failed++;
            if (entry.Error is not null) failures.Add($"{account.Name}: {entry.Error}");
            foreach (Confirmation confirmation in entry.Confirmations)
            {
                var item = new AccountConfirmation(account, confirmation) { ReceiverAvatarUrl = entry.ReceiverAvatarUrl };
                items.Add(item);
            }
        }

        AccountConfirmation[] ordered = items
            .OrderBy(item => item.AccountName, StringComparer.OrdinalIgnoreCase)
            .ThenByDescending(item => item.Confirmation.ID)
            .ToArray();
        return new ConfirmationLoadResult(
            ordered,
            checkedAccounts,
            expired,
            failed,
            refreshed,
            failures.OrderBy(value => value, StringComparer.OrdinalIgnoreCase).ToArray(),
            batch.Length,
            Math.Max(0, due.Count - batch.Length));
    }

    public async Task<ConfirmationActionResult> ApplyAsync(
        AccountConfirmation confirmation,
        ConfirmationDecision decision,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(confirmation);
        if (confirmation.Account.GuardAccount.Session is null ||
            confirmation.Account.GuardAccount.Session.IsRefreshTokenExpired())
        {
            return new ConfirmationActionResult(false);
        }

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            bool success = decision == ConfirmationDecision.Accept
                ? await confirmation.Account.GuardAccount.AcceptConfirmation(confirmation.Confirmation)
                : await confirmation.Account.GuardAccount.DenyConfirmation(confirmation.Confirmation);
            cancellationToken.ThrowIfCancellationRequested();
            if (success && _confirmationCache.TryGetValue(AccountKey(confirmation.Account), out ConfirmationCacheEntry? cached))
            {
                _confirmationCache[AccountKey(confirmation.Account)] = cached with
                {
                    Confirmations = cached.Confirmations.Where(item => item.ID != confirmation.Confirmation.ID).ToArray(),
                    CheckedAtUtc = DateTimeOffset.UtcNow
                };
            }
            return new ConfirmationActionResult(success);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return new ConfirmationActionResult(false);
        }
    }

    public async Task<ConfirmationDetailsResult> LoadDetailsAsync(
        AccountConfirmation confirmation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(confirmation);
        SteamGuardAccount guard = confirmation.Account.GuardAccount;
        if (guard.Session is null || guard.Session.IsRefreshTokenExpired())
        {
            return new ConfirmationDetailsResult(false, [], "Steam session expired");
        }

        try
        {
            if (guard.Session.IsAccessTokenExpired())
            {
                await guard.Session.RefreshAccessToken();
            }

            string tag = "details" + confirmation.Confirmation.ID;
            string url = $"https://steamcommunity.com/mobileconf/details/{confirmation.Confirmation.ID}?" +
                guard.GenerateConfirmationQueryParams(tag);
            using var handler = new HttpClientHandler
            {
                AutomaticDecompression = DecompressionMethods.All,
                CookieContainer = guard.Session.GetCookies()
            };
            using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(15) };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("SDA++/1.7 Confirmations");
            string json = await client.GetStringAsync(url, cancellationToken).ConfigureAwait(false);
            JObject response = JObject.Parse(json);
            if (response.Value<bool?>("success") != true)
            {
                return new ConfirmationDetailsResult(false, [], "Steam did not return trade details");
            }

            string html = response.Value<string>("html") ?? string.Empty;
            ConfirmationTradeItem[] detailItems = await ExtractTradeItemsAsync(client, html, cancellationToken);
            return new ConfirmationDetailsResult(true, detailItems);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            return new ConfirmationDetailsResult(false, [], ClassifyFailure(exception));
        }
    }

    private async Task<string?> LoadAccountAvatarAsync(VaultAccount account, CancellationToken cancellationToken)
    {
        if (!ulong.TryParse(account.SteamId, out ulong steamId) || steamId == 0) return null;
        Lazy<Task<string?>> lazy = _avatarCache.GetOrAdd(account.SteamId, _ =>
            new Lazy<Task<string?>>(() => FetchAccountAvatarAsync(steamId), LazyThreadSafetyMode.ExecutionAndPublication));
        cancellationToken.ThrowIfCancellationRequested();
        return await lazy.Value.ConfigureAwait(false);
    }

    private static async Task<string?> FetchAccountAvatarAsync(ulong steamId)
    {
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("SDA++/1.7 Confirmations");
            string xml = await client.GetStringAsync($"https://steamcommunity.com/profiles/{steamId}?xml=1").ConfigureAwait(false);
            Match match = Regex.Match(xml, "<avatarMedium><!\\[CDATA\\[(?<url>.*?)\\]\\]></avatarMedium>", RegexOptions.IgnoreCase);
            return match.Success ? WebUtility.HtmlDecode(match.Groups["url"].Value) : null;
        }
        catch
        {
            return null;
        }
    }

    private static async Task<ConfirmationTradeItem[]> ExtractTradeItemsAsync(
        HttpClient client,
        string html,
        CancellationToken cancellationToken)
    {
        int primaryStart = html.IndexOf("tradeoffer_items primary", StringComparison.OrdinalIgnoreCase);
        int secondaryStart = html.IndexOf("tradeoffer_items secondary", StringComparison.OrdinalIgnoreCase);
        if (primaryStart < 0 && secondaryStart < 0) return [];

        var references = new List<TradeItemReference>();
        if (primaryStart >= 0)
        {
            int end = secondaryStart > primaryStart ? secondaryStart : html.Length;
            references.AddRange(ParseTradeItemReferences(html[primaryStart..end], ConfirmationTradeSide.Give));
        }
        if (secondaryStart >= 0)
        {
            references.AddRange(ParseTradeItemReferences(html[secondaryStart..], ConfirmationTradeSide.Receive));
        }

        Task<ConfirmationTradeItem>[] tasks = references.Select(reference =>
            ResolveTradeItemAsync(client, reference, cancellationToken)).ToArray();
        return await Task.WhenAll(tasks);
    }

    private static IEnumerable<TradeItemReference> ParseTradeItemReferences(string html, ConfirmationTradeSide side)
    {
        const string pattern = "data-economy-item=[\\\"']classinfo/(?<app>\\d+)/(?<class>\\d+)/(?<instance>\\d+)[\\\"'][\\s\\S]*?<img[^>]+src=[\\\"'](?<image>[^\\\"']+)[\\\"']";
        foreach (Match match in Regex.Matches(html, pattern, RegexOptions.IgnoreCase))
        {
            yield return new TradeItemReference(
                match.Groups["app"].Value,
                match.Groups["class"].Value,
                match.Groups["instance"].Value,
                WebUtility.HtmlDecode(match.Groups["image"].Value),
                side);
        }
    }

    private static async Task<ConfirmationTradeItem> ResolveTradeItemAsync(
        HttpClient client,
        TradeItemReference reference,
        CancellationToken cancellationToken)
    {
        string name = "Steam item";
        string imageUrl = reference.ImageUrl;
        try
        {
            string url = $"https://steamcommunity.com/economy/itemclasshover/{reference.AppId}/{reference.ClassId}/{reference.InstanceId}?content_only=1&l=english";
            string html = await client.GetStringAsync(url, cancellationToken).ConfigureAwait(false);
            string? buildHover = html.Split('\n').FirstOrDefault(line => line.Contains("BuildHover(", StringComparison.Ordinal));
            if (buildHover is not null)
            {
                int jsonStart = buildHover.IndexOf('{');
                int jsonEnd = buildHover.LastIndexOf('}');
                if (jsonStart >= 0 && jsonEnd > jsonStart)
                {
                    JObject item = JObject.Parse(buildHover[jsonStart..(jsonEnd + 1)]);
                    name = item.Value<string>("market_name") ?? item.Value<string>("name") ?? name;
                    string? icon = item.Value<string>("icon_url");
                    if (!string.IsNullOrWhiteSpace(icon))
                    {
                        imageUrl = "https://community.akamai.steamstatic.com/economy/image/" + icon + "/192fx192f";
                    }
                }
            }
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or Newtonsoft.Json.JsonException)
        {
        }

        return new ConfirmationTradeItem(name, imageUrl, reference.Side);
    }

    private sealed record TradeItemReference(
        string AppId,
        string ClassId,
        string InstanceId,
        string ImageUrl,
        ConfirmationTradeSide Side);

    private static bool IsAuthenticationFailure(Exception exception) =>
        exception.Message.Contains("authentication", StringComparison.OrdinalIgnoreCase) ||
        exception.Message.Contains("login", StringComparison.OrdinalIgnoreCase);

    private static string ClassifyFailure(Exception exception)
    {
        string message = exception.Message;
        if (IsAuthenticationFailure(exception))
        {
            return "Steam authentication failed";
        }

        if (message.Contains("timed out", StringComparison.OrdinalIgnoreCase) ||
            exception is TimeoutException or HttpRequestException)
        {
            return "Steam connection failed";
        }

        return "confirmation request failed";
    }

    private static string AccountKey(VaultAccount account) =>
        string.IsNullOrWhiteSpace(account.SteamId) || account.SteamId == "SteamID unavailable"
            ? account.Name
            : account.SteamId;

    private static bool IsFresh(ConfirmationCacheEntry entry, DateTimeOffset now)
    {
        TimeSpan lifetime = entry.State switch
        {
            ConfirmationCacheState.Expired => TimeSpan.FromMinutes(5),
            ConfirmationCacheState.Failed => TimeSpan.FromSeconds(30),
            _ when entry.Confirmations.Length > 0 => TimeSpan.FromSeconds(20),
            _ => TimeSpan.FromMinutes(2)
        };
        return now - entry.CheckedAtUtc < lifetime;
    }

    private enum ConfirmationCacheState
    {
        Success,
        Expired,
        Failed
    }

    private sealed record ConfirmationCacheEntry(
        DateTimeOffset CheckedAtUtc,
        ConfirmationCacheState State,
        Confirmation[] Confirmations,
        string? ReceiverAvatarUrl,
        string? Error)
    {
        public static ConfirmationCacheEntry Success(DateTimeOffset at, Confirmation[] confirmations, string? avatarUrl) =>
            new(at, ConfirmationCacheState.Success, confirmations, avatarUrl, null);

        public static ConfirmationCacheEntry Expired(DateTimeOffset at, string error) =>
            new(at, ConfirmationCacheState.Expired, [], null, error);

        public static ConfirmationCacheEntry Failed(DateTimeOffset at, string error) =>
            new(at, ConfirmationCacheState.Failed, [], null, error);
    }
}
