using Newtonsoft.Json;

namespace SDAPlusPlus.Core.Vault;

public sealed class LegacyVaultAccountWriter(ILegacyVaultReader reader) : IVaultAccountWriter
{
    public VaultWriteResult SetFavorite(string maFilesDirectory, VaultAccount account, bool favorite)
    {
        if (!TryReadManifest(maFilesDirectory, out string? directory, out string? manifestPath, out LegacyVaultManifest? manifest, out VaultWriteResult? error))
        {
            return error!;
        }

        int entryIndex = FindEntryIndex(manifest!, account);
        if (entryIndex < 0)
        {
            return new VaultWriteResult(VaultWriteState.AccountNotFound, Detail: "The selected account is not present in manifest.json.");
        }

        LegacyVaultEntry original = manifest!.Entries[entryIndex];
        var updatedEntry = new LegacyVaultEntry
        {
            Filename = original.Filename,
            SteamId = original.SteamId,
            Favorite = favorite,
            Salt = original.Salt,
            Iv = original.Iv
        };
        var updatedManifest = new LegacyVaultManifest
        {
            Encrypted = manifest.Encrypted,
            Entries = manifest.Entries.Select((entry, index) => index == entryIndex ? updatedEntry : entry).ToList()
        };

        string backupDirectory = CreateBackupDirectory(directory!);
        string manifestBackup = Path.Combine(backupDirectory, "manifest.json");
        string manifestTemp = manifestPath! + ".sdapp.tmp";
        try
        {
            Directory.CreateDirectory(backupDirectory);
            File.Copy(manifestPath!, manifestBackup, overwrite: false);
            File.WriteAllText(manifestTemp, JsonConvert.SerializeObject(updatedManifest, Formatting.Indented));
            File.Move(manifestTemp, manifestPath!, overwrite: true);
            return new VaultWriteResult(VaultWriteState.Saved, backupDirectory);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            TryRestore(manifestBackup, manifestPath!);
            return new VaultWriteResult(VaultWriteState.Failed, backupDirectory, "The favorite update was rolled back after a file error.");
        }
        finally
        {
            TryDelete(manifestTemp);
        }
    }

    public VaultWriteResult SaveSession(string maFilesDirectory, VaultAccount account, string? vaultPassword = null)
    {
        string directory;
        try
        {
            directory = Path.GetFullPath(maFilesDirectory);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return new VaultWriteResult(VaultWriteState.Missing, Detail: "The vault path is invalid.");
        }

        string manifestPath = Path.Combine(directory, "manifest.json");
        if (!Directory.Exists(directory) || !File.Exists(manifestPath))
        {
            return new VaultWriteResult(VaultWriteState.Missing, Detail: "A manifest is required to update an account safely.");
        }

        LegacyVaultManifest? manifest;
        try
        {
            manifest = JsonConvert.DeserializeObject<LegacyVaultManifest>(File.ReadAllText(manifestPath));
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
        {
            return new VaultWriteResult(VaultWriteState.Corrupt, Detail: "manifest.json could not be read.");
        }

        if (manifest is null)
        {
            return new VaultWriteResult(VaultWriteState.Corrupt, Detail: "manifest.json is empty.");
        }

        if (manifest.Encrypted && string.IsNullOrEmpty(vaultPassword))
        {
            return new VaultWriteResult(VaultWriteState.Locked, Detail: "The vault password is required.");
        }

        VaultLoadResult validation = reader.Load(directory, vaultPassword);
        if (validation.State == VaultLoadState.InvalidPassword)
        {
            return new VaultWriteResult(VaultWriteState.InvalidPassword, Detail: "The vault password is incorrect.");
        }

        if (!validation.IsReady)
        {
            return new VaultWriteResult(VaultWriteState.Corrupt, Detail: validation.Detail ?? "The vault could not be validated.");
        }

        int entryIndex = FindEntryIndex(manifest, account);
        if (entryIndex < 0)
        {
            VaultAccount? validatedAccount = validation.Accounts.FirstOrDefault(candidate =>
                string.Equals(candidate.Name, account.Name, StringComparison.OrdinalIgnoreCase));
            if (validatedAccount is not null)
            {
                entryIndex = manifest.Entries.FindIndex(entry => entry.SteamId.ToString() == validatedAccount.SteamId);
            }
        }

        if (entryIndex < 0 || !TryResolveEntryPath(directory, manifest.Entries[entryIndex].Filename, out string? accountPath) || !File.Exists(accountPath))
        {
            return new VaultWriteResult(VaultWriteState.AccountNotFound, Detail: "The selected account is not present in manifest.json.");
        }

        string accountJson = JsonConvert.SerializeObject(account.GuardAccount, Formatting.Indented);
        string accountContents = accountJson;
        LegacyVaultEntry originalEntry = manifest.Entries[entryIndex];
        LegacyVaultEntry updatedEntry = originalEntry;
        if (manifest.Encrypted)
        {
            (string cipherText, string salt, string iv) = LegacyVaultCrypto.Encrypt(vaultPassword!, accountJson);
            accountContents = cipherText;
            updatedEntry = new LegacyVaultEntry
            {
                Filename = originalEntry.Filename,
                SteamId = originalEntry.SteamId,
                Favorite = originalEntry.Favorite,
                Salt = salt,
                Iv = iv
            };
        }

        var updatedManifest = new LegacyVaultManifest
        {
            Encrypted = manifest.Encrypted,
            Entries = manifest.Entries.Select((entry, index) => index == entryIndex ? updatedEntry : entry).ToList()
        };

        string backupDirectory = CreateBackupDirectory(directory);
        string accountBackup = Path.Combine(backupDirectory, Path.GetFileName(accountPath));
        string manifestBackup = Path.Combine(backupDirectory, "manifest.json");
        string accountTemp = accountPath + ".sdapp.tmp";
        string manifestTemp = manifestPath + ".sdapp.tmp";
        try
        {
            Directory.CreateDirectory(backupDirectory);
            File.Copy(accountPath, accountBackup, overwrite: false);
            File.Copy(manifestPath, manifestBackup, overwrite: false);

            DurableTextFile.Write(accountTemp, accountContents);
            DurableTextFile.Write(manifestTemp, JsonConvert.SerializeObject(updatedManifest, Formatting.Indented));
            File.Move(accountTemp, accountPath, overwrite: true);
            File.Move(manifestTemp, manifestPath, overwrite: true);
            return new VaultWriteResult(VaultWriteState.Saved, backupDirectory);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            TryRestore(accountBackup, accountPath);
            TryRestore(manifestBackup, manifestPath);
            return new VaultWriteResult(VaultWriteState.Failed, backupDirectory, "The session update was rolled back after a file error.");
        }
        finally
        {
            TryDelete(accountTemp);
            TryDelete(manifestTemp);
        }
    }

    private static string CreateBackupDirectory(string vaultDirectory)
    {
        string parent = Directory.GetParent(vaultDirectory)?.FullName ?? vaultDirectory;
        return Path.Combine(parent, "SDAPlusPlus-backups", $"session-{DateTime.UtcNow:yyyyMMdd-HHmmssfff}-{Guid.NewGuid():N}");
    }

    private static int FindEntryIndex(LegacyVaultManifest manifest, VaultAccount account) =>
        manifest.Entries.FindIndex(entry =>
            (entry.SteamId > 0 && entry.SteamId.ToString() == account.SteamId) ||
            string.Equals(Path.GetFileNameWithoutExtension(entry.Filename), account.SteamId, StringComparison.OrdinalIgnoreCase));

    private static bool TryReadManifest(
        string maFilesDirectory,
        out string? directory,
        out string? manifestPath,
        out LegacyVaultManifest? manifest,
        out VaultWriteResult? error)
    {
        directory = null;
        manifestPath = null;
        manifest = null;
        error = null;
        try
        {
            directory = Path.GetFullPath(maFilesDirectory);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            error = new VaultWriteResult(VaultWriteState.Missing, Detail: "The vault path is invalid.");
            return false;
        }

        manifestPath = Path.Combine(directory, "manifest.json");
        if (!Directory.Exists(directory) || !File.Exists(manifestPath))
        {
            error = new VaultWriteResult(VaultWriteState.Missing, Detail: "A manifest is required to update an account safely.");
            return false;
        }

        try
        {
            manifest = JsonConvert.DeserializeObject<LegacyVaultManifest>(File.ReadAllText(manifestPath));
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
        {
            error = new VaultWriteResult(VaultWriteState.Corrupt, Detail: "manifest.json could not be read.");
            return false;
        }

        if (manifest is null)
        {
            error = new VaultWriteResult(VaultWriteState.Corrupt, Detail: "manifest.json is empty.");
            return false;
        }

        return true;
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
        if (!candidate.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        path = candidate;
        return true;
    }

    private static void TryRestore(string backupPath, string targetPath)
    {
        try
        {
            if (File.Exists(backupPath))
            {
                File.Copy(backupPath, targetPath, overwrite: true);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }
}
