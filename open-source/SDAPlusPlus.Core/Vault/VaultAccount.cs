using SteamAuth;

namespace SDAPlusPlus.Core.Vault;

public sealed record VaultAccount(
    string Name,
    string SteamId,
    SteamGuardAccount GuardAccount,
    bool HasMobileSession,
    bool IsFavorite);
