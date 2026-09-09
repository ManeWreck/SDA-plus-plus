using SDAPlusPlus.Core.Vault;

namespace SDAPlusPlus.Core.Monitoring;

public interface IAccountMonitoringService
{
    Task<AccountMonitoringSnapshot> LoadSnapshotAsync(
        VaultAccount account,
        bool forceRefresh = false,
        CancellationToken cancellationToken = default);
}

public enum MonitoredSessionHealth
{
    Missing,
    Active,
    AccessExpired,
    RefreshExpired
}

public enum MonitoredInventoryState
{
    Available,
    Private,
    RateLimited,
    Unavailable
}

public sealed record AccountMonitoringSnapshot(
    VaultAccount Account,
    MonitoredSessionHealth SessionHealth,
    string VacStatus,
    int? SteamLevel,
    int? GameCount,
    MonitoredInventoryState InventoryState,
    int? TradableItemCount,
    bool IsPrivateProfile,
    DateTimeOffset LoadedAtUtc,
    string? Error = null,
    bool IsCached = false);
