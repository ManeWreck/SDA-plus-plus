using Newtonsoft.Json;

namespace SDAPlusPlus.Core.Vault;

internal sealed class LegacyVaultManifest
{
    [JsonProperty("encrypted")]
    public bool Encrypted { get; init; }

    [JsonProperty("entries")]
    public List<LegacyVaultEntry> Entries { get; init; } = [];
}

internal sealed class LegacyVaultEntry
{
    [JsonProperty("encryption_iv")]
    public string? Iv { get; init; }

    [JsonProperty("encryption_salt")]
    public string? Salt { get; init; }

    [JsonProperty("filename")]
    public string Filename { get; init; } = string.Empty;

    [JsonProperty("steamid")]
    public ulong SteamId { get; init; }

    [JsonProperty("favorite")]
    public bool Favorite { get; init; }
}
