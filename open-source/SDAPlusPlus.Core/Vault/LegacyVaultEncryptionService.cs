using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace SDAPlusPlus.Core.Vault;

public sealed class LegacyVaultEncryptionService(ILegacyVaultReader reader) : IVaultEncryptionService
{
    public VaultEncryptionStatus Inspect(string maFilesDirectory)
    {
        if (!TryResolveVault(maFilesDirectory, out string? directory, out string? manifestPath, out JObject? manifest, out string? error))
        {
            return new VaultEncryptionStatus(false, false, 0, error);
        }

        bool encrypted = manifest!.Value<bool?>("encrypted") ?? false;
        int count = (manifest["entries"] as JArray)?.Count ?? 0;
        return new VaultEncryptionStatus(true, encrypted, count);
    }

    public VaultEncryptionResult ChangeProtection(
        string maFilesDirectory,
        string? currentPassword,
        string? newPassword)
    {
        if (!TryResolveVault(maFilesDirectory, out string? directory, out string? manifestPath, out JObject? manifest, out string? error))
        {
            return new VaultEncryptionResult(VaultEncryptionState.Missing, false, Detail: error);
        }

        bool wasEncrypted = manifest!.Value<bool?>("encrypted") ?? false;
        bool willBeEncrypted = !string.IsNullOrEmpty(newPassword);
        if (wasEncrypted && string.IsNullOrEmpty(currentPassword))
        {
            return new VaultEncryptionResult(VaultEncryptionState.InvalidPassword, true, Detail: "The current vault password is required.");
        }

        if (wasEncrypted == willBeEncrypted &&
            (!wasEncrypted || string.Equals(currentPassword, newPassword, StringComparison.Ordinal)))
        {
            return new VaultEncryptionResult(VaultEncryptionState.InvalidRequest, wasEncrypted, Detail: "The requested protection is already active.");
        }

        VaultLoadResult sourceValidation = reader.Load(directory!, currentPassword);
        if (sourceValidation.State == VaultLoadState.InvalidPassword)
        {
            return new VaultEncryptionResult(VaultEncryptionState.InvalidPassword, wasEncrypted, Detail: "The current vault password is incorrect.");
        }

        if (!sourceValidation.IsReady)
        {
            return new VaultEncryptionResult(VaultEncryptionState.Corrupt, wasEncrypted, Detail: sourceValidation.Detail ?? "The vault could not be validated.");
        }

        if (manifest["entries"] is not JArray entries)
        {
            return new VaultEncryptionResult(VaultEncryptionState.Corrupt, wasEncrypted, Detail: "manifest.json has no entries array.");
        }

        string parent = Directory.GetParent(directory!)?.FullName ?? directory!;
        string operationId = $"protection-{DateTime.UtcNow:yyyyMMdd-HHmmssfff}-{Guid.NewGuid():N}";
        string stagingDirectory = Path.Combine(parent, "SDAPlusPlus-staging", operationId);
        string backupDirectory = Path.Combine(parent, "SDAPlusPlus-backups", operationId);
        var sourceFiles = new List<(string Source, string Relative)>();

        try
        {
            Directory.CreateDirectory(stagingDirectory);
            JObject stagedManifest = (JObject)manifest.DeepClone();
            JArray stagedEntries = (JArray)stagedManifest["entries"]!;

            for (int index = 0; index < entries.Count; index++)
            {
                if (entries[index] is not JObject sourceEntry || stagedEntries[index] is not JObject stagedEntry)
                {
                    return Corrupt(wasEncrypted, "manifest.json contains an invalid account entry.");
                }

                string relative = sourceEntry.Value<string>("filename") ?? string.Empty;
                if (!TryResolveEntryPath(directory!, relative, out string? sourcePath) || !File.Exists(sourcePath))
                {
                    return Corrupt(wasEncrypted, $"Account file '{relative}' is missing or outside the vault.");
                }

                string contents = File.ReadAllText(sourcePath!);
                if (wasEncrypted && !LegacyVaultCrypto.TryDecrypt(
                        currentPassword!,
                        sourceEntry.Value<string>("encryption_salt") ?? string.Empty,
                        sourceEntry.Value<string>("encryption_iv") ?? string.Empty,
                        contents,
                        out contents))
                {
                    return new VaultEncryptionResult(VaultEncryptionState.InvalidPassword, true, Detail: "The current vault password is incorrect.");
                }

                if (!IsAccountJson(contents))
                {
                    return Corrupt(wasEncrypted, $"Account file '{relative}' is not a valid .maFile.");
                }

                string stagedContents = contents;
                if (willBeEncrypted)
                {
                    (string cipherText, string salt, string iv) = LegacyVaultCrypto.Encrypt(newPassword!, contents);
                    stagedContents = cipherText;
                    stagedEntry["encryption_salt"] = salt;
                    stagedEntry["encryption_iv"] = iv;
                }
                else
                {
                    stagedEntry["encryption_salt"] = null;
                    stagedEntry["encryption_iv"] = null;
                }

                string stagedPath = Path.Combine(stagingDirectory, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(stagedPath)!);
                File.WriteAllText(stagedPath, stagedContents);
                sourceFiles.Add((sourcePath!, relative));
            }

            stagedManifest["encrypted"] = willBeEncrypted;
            File.WriteAllText(
                Path.Combine(stagingDirectory, "manifest.json"),
                stagedManifest.ToString(Formatting.Indented));

            VaultLoadResult stagedValidation = reader.Load(stagingDirectory, newPassword);
            if (!stagedValidation.IsReady || stagedValidation.Accounts.Count != sourceValidation.Accounts.Count)
            {
                return new VaultEncryptionResult(
                    VaultEncryptionState.Failed,
                    wasEncrypted,
                    Detail: "The staged vault did not pass verification. No original files were changed.");
            }

            Directory.CreateDirectory(backupDirectory);
            File.Copy(manifestPath!, Path.Combine(backupDirectory, "manifest.json"), overwrite: false);
            foreach ((string sourcePath, string relative) in sourceFiles)
            {
                string backupPath = Path.Combine(backupDirectory, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(backupPath)!);
                File.Copy(sourcePath, backupPath, overwrite: false);
            }

            try
            {
                foreach ((string sourcePath, string relative) in sourceFiles)
                {
                    File.Copy(Path.Combine(stagingDirectory, relative), sourcePath, overwrite: true);
                }

                File.Copy(Path.Combine(stagingDirectory, "manifest.json"), manifestPath!, overwrite: true);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                RestoreBackup(backupDirectory, directory!, sourceFiles.Select(item => item.Relative));
                return new VaultEncryptionResult(
                    VaultEncryptionState.Failed,
                    wasEncrypted,
                    sourceValidation.Accounts.Count,
                    backupDirectory,
                    "The update failed and the original vault was restored.");
            }

            VaultLoadResult finalValidation = reader.Load(directory!, newPassword);
            if (!finalValidation.IsReady || finalValidation.Accounts.Count != sourceValidation.Accounts.Count)
            {
                RestoreBackup(backupDirectory, directory!, sourceFiles.Select(item => item.Relative));
                return new VaultEncryptionResult(
                    VaultEncryptionState.Failed,
                    wasEncrypted,
                    sourceValidation.Accounts.Count,
                    backupDirectory,
                    "Final verification failed and the original vault was restored.");
            }

            return new VaultEncryptionResult(
                VaultEncryptionState.Changed,
                willBeEncrypted,
                finalValidation.Accounts.Count,
                backupDirectory);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return new VaultEncryptionResult(
                VaultEncryptionState.Failed,
                wasEncrypted,
                sourceValidation.Accounts.Count,
                Directory.Exists(backupDirectory) ? backupDirectory : null,
                "The vault could not be updated. No unverified data was committed.");
        }
        finally
        {
            TryDeleteDirectory(stagingDirectory);
        }
    }

    private static VaultEncryptionResult Corrupt(bool encrypted, string detail) =>
        new(VaultEncryptionState.Corrupt, encrypted, Detail: detail);

    private static bool TryResolveVault(
        string maFilesDirectory,
        out string? directory,
        out string? manifestPath,
        out JObject? manifest,
        out string? error)
    {
        directory = null;
        manifestPath = null;
        manifest = null;
        error = null;
        try
        {
            directory = Path.GetFullPath(maFilesDirectory);
            manifestPath = Path.Combine(directory, "manifest.json");
            if (!Directory.Exists(directory) || !File.Exists(manifestPath))
            {
                error = "A manifest-based maFiles vault is required.";
                return false;
            }

            manifest = JObject.Parse(File.ReadAllText(manifestPath));
            return true;
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException or IOException or UnauthorizedAccessException or JsonException)
        {
            error = "manifest.json could not be read.";
            return false;
        }
    }

    private static bool TryResolveEntryPath(string rootDirectory, string relative, out string? path)
    {
        path = null;
        if (string.IsNullOrWhiteSpace(relative) || Path.IsPathRooted(relative))
        {
            return false;
        }

        string root = Path.GetFullPath(rootDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string candidate = Path.GetFullPath(Path.Combine(root, relative));
        if (!candidate.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        path = candidate;
        return true;
    }

    private static bool IsAccountJson(string contents)
    {
        try
        {
            JObject account = JObject.Parse(contents);
            return !string.IsNullOrWhiteSpace(account.Value<string>("account_name")) &&
                   !string.IsNullOrWhiteSpace(account.Value<string>("shared_secret"));
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static void RestoreBackup(string backupDirectory, string vaultDirectory, IEnumerable<string> relatives)
    {
        foreach (string relative in relatives)
        {
            string backup = Path.Combine(backupDirectory, relative);
            if (File.Exists(backup))
            {
                File.Copy(backup, Path.Combine(vaultDirectory, relative), overwrite: true);
            }
        }

        string manifestBackup = Path.Combine(backupDirectory, "manifest.json");
        if (File.Exists(manifestBackup))
        {
            File.Copy(manifestBackup, Path.Combine(vaultDirectory, "manifest.json"), overwrite: true);
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }
}
