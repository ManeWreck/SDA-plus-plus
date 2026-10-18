using Newtonsoft.Json;
using SteamAuth;

namespace SDAPlusPlus.Core.Vault;

public sealed class LegacyVaultReader : ILegacyVaultReader
{
    public VaultLoadResult Load(string maFilesDirectory, string? password = null)
    {
        string directory;
        try
        {
            directory = Path.GetFullPath(maFilesDirectory);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return new VaultLoadResult(VaultLoadState.Missing, [], maFilesDirectory, Detail: "The vault path is invalid.");
        }

        if (!Directory.Exists(directory))
        {
            return new VaultLoadResult(VaultLoadState.Missing, [], directory);
        }

        string manifestPath = Path.Combine(directory, "manifest.json");
        if (!File.Exists(manifestPath))
        {
            return LoadWithoutManifest(directory);
        }

        LegacyVaultManifest? manifest;
        try
        {
            manifest = JsonConvert.DeserializeObject<LegacyVaultManifest>(File.ReadAllText(manifestPath));
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
        {
            return new VaultLoadResult(VaultLoadState.Corrupt, [], directory, Detail: "manifest.json could not be read.");
        }

        if (manifest is null)
        {
            return new VaultLoadResult(VaultLoadState.Corrupt, [], directory, Detail: "manifest.json is empty.");
        }

        if (manifest.Encrypted && string.IsNullOrEmpty(password))
        {
            return new VaultLoadResult(VaultLoadState.Locked, [], directory);
        }

        var accounts = new List<VaultAccount>();
        int skipped = 0;
        foreach (LegacyVaultEntry entry in manifest.Entries)
        {
            if (!TryResolveEntryPath(directory, entry.Filename, out string? accountPath) || !File.Exists(accountPath))
            {
                skipped++;
                continue;
            }

            string contents;
            try
            {
                contents = File.ReadAllText(accountPath);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                skipped++;
                continue;
            }

            if (manifest.Encrypted)
            {
                if (!LegacyVaultCrypto.TryDecrypt(password!, entry.Salt ?? string.Empty, entry.Iv ?? string.Empty, contents, out contents))
                {
                    return new VaultLoadResult(VaultLoadState.InvalidPassword, [], directory);
                }
            }

            if (!TryDeserializeAccount(contents, entry.SteamId, entry.Favorite, out VaultAccount? account))
            {
                if (manifest.Encrypted)
                {
                    return new VaultLoadResult(VaultLoadState.InvalidPassword, [], directory);
                }

                skipped++;
                continue;
            }

            accounts.Add(account!);
        }

        VaultLoadState state = skipped > 0 ? VaultLoadState.Partial : VaultLoadState.Ready;
        return new VaultLoadResult(state, accounts, directory, skipped);
    }

    private static VaultLoadResult LoadWithoutManifest(string directory)
    {
        var accounts = new List<VaultAccount>();
        int skipped = 0;

        foreach (string path in Directory.EnumerateFiles(directory, "*.maFile", SearchOption.TopDirectoryOnly).OrderBy(Path.GetFileName))
        {
            try
            {
                string contents = File.ReadAllText(path);
                ulong steamId = ulong.TryParse(Path.GetFileNameWithoutExtension(path), out ulong parsed) ? parsed : 0;
                if (TryDeserializeAccount(contents, steamId, false, out VaultAccount? account))
                {
                    accounts.Add(account!);
                }
                else
                {
                    skipped++;
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                skipped++;
            }
        }

        if (accounts.Count == 0 && skipped > 0)
        {
            return new VaultLoadResult(VaultLoadState.Corrupt, [], directory, skipped, "A manifest is required for encrypted accounts.");
        }

        return new VaultLoadResult(skipped > 0 ? VaultLoadState.Partial : VaultLoadState.Ready, accounts, directory, skipped);
    }

    private static bool TryDeserializeAccount(string json, ulong manifestSteamId, bool favorite, out VaultAccount? result)
    {
        result = null;
        try
        {
            SteamGuardAccount? account = JsonConvert.DeserializeObject<SteamGuardAccount>(json);
            if (account is null || string.IsNullOrWhiteSpace(account.AccountName) || string.IsNullOrWhiteSpace(account.SharedSecret))
            {
                return false;
            }

            ulong steamId = account.Session?.SteamID > 0 ? account.Session.SteamID : manifestSteamId;
            bool hasSession = !string.IsNullOrWhiteSpace(account.Session?.AccessToken);
            result = new VaultAccount(account.AccountName, steamId > 0 ? steamId.ToString() : "SteamID unavailable", account, hasSession, favorite);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool TryResolveEntryPath(string rootDirectory, string filename, out string? path)
    {
        path = null;
        if (string.IsNullOrWhiteSpace(filename) || Path.IsPathRooted(filename))
        {
            return false;
        }

        string root = Path.GetFullPath(rootDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string candidate = Path.GetFullPath(Path.Combine(root, filename));
        string prefix = root + Path.DirectorySeparatorChar;
        if (!candidate.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        path = candidate;
        return true;
    }
}
