using Newtonsoft.Json;
using SteamAuth;

namespace SDAPlusPlus.Core.Vault;

public sealed class LegacyVaultAccountImportService(ILegacyVaultReader reader) : IVaultAccountImportService
{
    public VaultAccountImportResult Import(
        string sourceMaFile,
        string destinationDirectory,
        string? sourcePassword = null,
        string? destinationPassword = null)
    {
        if (!TryResolveSource(sourceMaFile, out string? sourcePath) || !File.Exists(sourcePath))
        {
            return new VaultAccountImportResult(VaultAccountImportState.SourceMissing);
        }

        VaultAccountImportResult sourceResult = ReadSourceAccount(sourcePath!, sourcePassword, out VaultAccount? sourceAccount);
        if (!sourceResult.IsSuccess || sourceAccount is null)
        {
            return sourceResult;
        }

        return ImportAccount(sourceAccount, destinationDirectory, destinationPassword);
    }

    public VaultAccountImportResult ImportAccount(VaultAccount sourceAccount, string destinationDirectory, string? destinationPassword = null)
    {
        if (!IsValid(sourceAccount.GuardAccount))
            return new VaultAccountImportResult(VaultAccountImportState.InvalidAccount);

        string destination;
        try
        {
            destination = Path.GetFullPath(destinationDirectory);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return new VaultAccountImportResult(VaultAccountImportState.DestinationCorrupt, Detail: "The destination vault path is invalid.");
        }

        Directory.CreateDirectory(destination);
        string manifestPath = Path.Combine(destination, "manifest.json");
        LegacyVaultManifest manifest;
        if (File.Exists(manifestPath))
        {
            try
            {
                manifest = JsonConvert.DeserializeObject<LegacyVaultManifest>(File.ReadAllText(manifestPath))
                    ?? throw new JsonException("manifest.json is empty.");
            }
            catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
            {
                return new VaultAccountImportResult(VaultAccountImportState.DestinationCorrupt, Detail: "The destination manifest could not be read.");
            }

            if (manifest.Encrypted && string.IsNullOrEmpty(destinationPassword))
            {
                return new VaultAccountImportResult(VaultAccountImportState.DestinationLocked, sourceAccount.Name);
            }

            VaultLoadResult validation = reader.Load(destination, destinationPassword);
            if (validation.State == VaultLoadState.InvalidPassword)
            {
                return new VaultAccountImportResult(VaultAccountImportState.InvalidDestinationPassword, sourceAccount.Name);
            }

            if (validation.State != VaultLoadState.Ready)
            {
                return new VaultAccountImportResult(VaultAccountImportState.DestinationCorrupt, sourceAccount.Name, Detail: validation.Detail);
            }

            if (validation.Accounts.Any(account =>
                    account.SteamId == sourceAccount.SteamId ||
                    string.Equals(account.Name, sourceAccount.Name, StringComparison.OrdinalIgnoreCase)))
            {
                return new VaultAccountImportResult(VaultAccountImportState.Duplicate, sourceAccount.Name);
            }
        }
        else
        {
            manifest = new LegacyVaultManifest { Encrypted = !string.IsNullOrEmpty(destinationPassword), Entries = [] };
        }

        string filename = CreateFilename(sourceAccount, manifest);
        while (File.Exists(Path.Combine(destination, filename)) || Directory.Exists(Path.Combine(destination, filename)))
        {
            filename = Guid.NewGuid().ToString("N") + ".maFile";
        }
        string targetPath = Path.Combine(destination, filename);
        string json = JsonConvert.SerializeObject(sourceAccount.GuardAccount, Formatting.Indented);
        string contents = json;
        string? salt = null;
        string? iv = null;
        if (manifest.Encrypted)
        {
            (contents, salt, iv) = LegacyVaultCrypto.Encrypt(destinationPassword!, json);
        }

        ulong steamId = ulong.TryParse(sourceAccount.SteamId, out ulong parsedSteamId) ? parsedSteamId : 0;
        var entry = new LegacyVaultEntry
        {
            Filename = filename,
            SteamId = steamId,
            Favorite = sourceAccount.IsFavorite,
            Salt = salt,
            Iv = iv
        };
        var updatedManifest = new LegacyVaultManifest
        {
            Encrypted = manifest.Encrypted,
            Entries = [.. manifest.Entries, entry]
        };

        string backupDirectory = CreateBackupDirectory(destination);
        string manifestBackup = Path.Combine(backupDirectory, "manifest.json");
        string operationId = Guid.NewGuid().ToString("N");
        string accountTemp = targetPath + "." + operationId + ".tmp";
        string manifestTemp = manifestPath + "." + operationId + ".tmp";
        bool accountCreated = false;
        try
        {
            Directory.CreateDirectory(backupDirectory);
            if (File.Exists(manifestPath))
            {
                File.Copy(manifestPath, manifestBackup, overwrite: false);
            }

            DurableTextFile.Write(accountTemp, contents);
            DurableTextFile.Write(manifestTemp, JsonConvert.SerializeObject(updatedManifest, Formatting.Indented));
            File.Move(accountTemp, targetPath, overwrite: false);
            accountCreated = true;
            File.Move(manifestTemp, manifestPath, overwrite: true);
            return new VaultAccountImportResult(VaultAccountImportState.Imported, sourceAccount.Name, backupDirectory);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // The atomic manifest move is the commit point. On failure it is unchanged;
            // only remove the new account file if this operation actually created it.
            if (accountCreated)
            {
                TryDelete(targetPath);
            }

            return new VaultAccountImportResult(VaultAccountImportState.Failed, sourceAccount.Name, backupDirectory, "The account import was rolled back after a file error.");
        }
        finally
        {
            TryDelete(accountTemp);
            TryDelete(manifestTemp);
        }
    }

    private VaultAccountImportResult ReadSourceAccount(string sourcePath, string? password, out VaultAccount? account)
    {
        account = null;
        try
        {
            string contents = File.ReadAllText(sourcePath);
            SteamGuardAccount? guard = JsonConvert.DeserializeObject<SteamGuardAccount>(contents);
            if (IsValid(guard))
            {
                ulong steamId = guard!.Session?.SteamID ?? 0;
                account = new VaultAccount(guard.AccountName, steamId > 0 ? steamId.ToString() : "SteamID unavailable", guard, !string.IsNullOrWhiteSpace(guard.Session?.AccessToken), false);
                return new VaultAccountImportResult(VaultAccountImportState.Imported, account.Name);
            }
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
        {
        }

        string sourceDirectory = Path.GetDirectoryName(sourcePath)!;
        string manifestPath = Path.Combine(sourceDirectory, "manifest.json");
        if (!File.Exists(manifestPath))
        {
            return new VaultAccountImportResult(VaultAccountImportState.InvalidAccount, Detail: "Encrypted .maFile imports require the matching manifest.json beside the source file.");
        }

        LegacyVaultManifest? sourceManifest;
        try
        {
            sourceManifest = JsonConvert.DeserializeObject<LegacyVaultManifest>(File.ReadAllText(manifestPath));
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
        {
            return new VaultAccountImportResult(VaultAccountImportState.InvalidAccount, Detail: "The source manifest could not be read.");
        }

        LegacyVaultEntry? sourceEntry = sourceManifest?.Entries.FirstOrDefault(entry =>
            string.Equals(Path.GetFileName(entry.Filename), Path.GetFileName(sourcePath), StringComparison.OrdinalIgnoreCase));
        if (sourceEntry is null)
        {
            return new VaultAccountImportResult(VaultAccountImportState.InvalidAccount, Detail: "The source manifest does not reference this .maFile.");
        }

        if (sourceManifest!.Encrypted && string.IsNullOrEmpty(password))
        {
            return new VaultAccountImportResult(VaultAccountImportState.SourceLocked);
        }

        VaultLoadResult load = reader.Load(sourceDirectory, password);
        if (load.State == VaultLoadState.InvalidPassword)
        {
            return new VaultAccountImportResult(VaultAccountImportState.InvalidSourcePassword);
        }

        account = load.Accounts.FirstOrDefault(candidate => candidate.SteamId == sourceEntry.SteamId.ToString());
        return account is null
            ? new VaultAccountImportResult(VaultAccountImportState.InvalidAccount, Detail: "The selected source account could not be decoded.")
            : new VaultAccountImportResult(VaultAccountImportState.Imported, account.Name);
    }

    private static bool TryResolveSource(string sourceMaFile, out string? path)
    {
        path = null;
        try
        {
            path = Path.GetFullPath(sourceMaFile);
            return string.Equals(Path.GetExtension(path), ".maFile", StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }

    private static bool IsValid(SteamGuardAccount? account) =>
        account is not null && !string.IsNullOrWhiteSpace(account.AccountName) && !string.IsNullOrWhiteSpace(account.SharedSecret);

    private static string CreateFilename(VaultAccount account, LegacyVaultManifest manifest)
    {
        string stem = ulong.TryParse(account.SteamId, out ulong steamId) && steamId > 0
            ? steamId.ToString()
            : string.Concat(account.Name.Where(character => char.IsLetterOrDigit(character) || character is '-' or '_'));
        if (string.IsNullOrWhiteSpace(stem))
        {
            stem = "account";
        }

        string filename = stem + ".maFile";
        return manifest.Entries.Any(entry => string.Equals(entry.Filename, filename, StringComparison.OrdinalIgnoreCase))
            ? stem + "-" + Guid.NewGuid().ToString("N") + ".maFile"
            : filename;
    }

    private static string CreateBackupDirectory(string destination) =>
        Path.Combine(Directory.GetParent(destination)?.FullName ?? destination, "SDAPlusPlus-backups", $"import-{DateTime.UtcNow:yyyyMMdd-HHmmssfff}-{Guid.NewGuid():N}");

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
