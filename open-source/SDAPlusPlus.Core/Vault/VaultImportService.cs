using Newtonsoft.Json.Linq;
using SDAPlusPlus.Core.Cloud;

namespace SDAPlusPlus.Core.Vault;

public sealed class VaultImportService(ILegacyVaultReader vaultReader) : IVaultImportService
{
    public VaultImportResult Commit(CloudPullPackage package, string maFilesDirectory)
    {
        ArgumentNullException.ThrowIfNull(package);
        string directory = Path.GetFullPath(maFilesDirectory);
        Directory.CreateDirectory(directory);

        if (!TryValidatePackage(package, out string validationError))
        {
            return new VaultImportResult(false, validationError);
        }

        string stageDirectory = Path.Combine(directory, ".pull-stage-" + Guid.NewGuid().ToString("N"));
        string backupDirectory = Path.Combine(
            directory,
            "backups",
            "cloudsync",
            DateTime.UtcNow.ToString("yyyyMMdd-HHmmssfff") + "-pull");

        try
        {
            Directory.CreateDirectory(stageDirectory);
            foreach ((string fileName, string contents) in package.AccountFiles)
            {
                File.WriteAllText(Path.Combine(stageDirectory, fileName), contents);
            }
            File.WriteAllText(Path.Combine(stageDirectory, "manifest.json"), package.ManifestJson);

            VaultLoadResult stagedResult = vaultReader.Load(stageDirectory);
            bool stageValid = package.IsEncrypted
                ? stagedResult.State == VaultLoadState.Locked
                : stagedResult.IsReady && stagedResult.Accounts.Count == package.AccountFiles.Count;
            if (!stageValid)
            {
                return new VaultImportResult(false, "Downloaded vault failed local validation.");
            }

            Directory.CreateDirectory(backupDirectory);
            foreach (string existing in Directory.EnumerateFiles(directory, "*.maFile", SearchOption.TopDirectoryOnly))
            {
                File.Copy(existing, Path.Combine(backupDirectory, Path.GetFileName(existing)), overwrite: true);
            }
            string localManifest = Path.Combine(directory, "manifest.json");
            if (File.Exists(localManifest))
            {
                File.Copy(localManifest, Path.Combine(backupDirectory, "manifest.json"), overwrite: true);
            }

            var destinations = package.AccountFiles.Keys
                .Append("manifest.json")
                .ToDictionary(name => name, name => File.Exists(Path.Combine(directory, name)), StringComparer.OrdinalIgnoreCase);

            try
            {
                foreach (string fileName in package.AccountFiles.Keys)
                {
                    File.Move(Path.Combine(stageDirectory, fileName), Path.Combine(directory, fileName), overwrite: true);
                }

                // The manifest is committed last so it never points at files that were not written.
                File.Move(Path.Combine(stageDirectory, "manifest.json"), localManifest, overwrite: true);
            }
            catch
            {
                RestoreDestinations(directory, backupDirectory, destinations);
                throw;
            }

            return new VaultImportResult(
                true,
                "Cloud backup imported successfully.",
                package.AccountFiles.Count,
                backupDirectory);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return new VaultImportResult(false, "Cloud import failed: " + exception.Message, BackupDirectory: backupDirectory);
        }
        finally
        {
            try
            {
                if (Directory.Exists(stageDirectory))
                {
                    Directory.Delete(stageDirectory, recursive: true);
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // The committed vault is valid even if a scanner temporarily locks the staging folder.
            }
        }
    }

    private static bool TryValidatePackage(CloudPullPackage package, out string error)
    {
        error = string.Empty;
        JObject manifest;
        try
        {
            manifest = JObject.Parse(package.ManifestJson);
        }
        catch (Newtonsoft.Json.JsonException)
        {
            error = "Downloaded manifest is invalid.";
            return false;
        }

        string[] manifestFiles = (manifest["entries"] as JArray ?? [])
            .Select(entry => entry?["filename"]?.Value<string>() ?? string.Empty)
            .ToArray();
        if (manifestFiles.Length != package.AccountFiles.Count || manifestFiles.Any(name => !IsSafeFileName(name)))
        {
            error = "Downloaded manifest contains invalid account entries.";
            return false;
        }

        if (package.AccountFiles.Keys.Any(name => !IsSafeFileName(name)) ||
            manifestFiles.Any(name => !package.AccountFiles.ContainsKey(name)) ||
            package.AccountFiles.Values.Any(string.IsNullOrWhiteSpace))
        {
            error = "Downloaded account package is incomplete or unsafe.";
            return false;
        }

        bool manifestEncrypted = manifest.Value<bool?>("encrypted") ?? false;
        if (manifestEncrypted != package.IsEncrypted)
        {
            error = "Downloaded manifest encryption state does not match the package.";
            return false;
        }

        if (package.IsEncrypted)
        {
            bool invalidMetadata = (manifest["entries"] as JArray ?? [])
                .Any(entry => !IsValidBase64(entry?["encryption_salt"]?.Value<string>(), expectedBytes: null) ||
                              !IsValidBase64(entry?["encryption_iv"]?.Value<string>(), expectedBytes: 16));
            if (invalidMetadata || package.AccountFiles.Values.Any(contents => !IsValidCipherText(contents)))
            {
                error = "Downloaded encrypted vault contains invalid encryption data.";
                return false;
            }
        }

        return true;
    }

    private static bool IsValidCipherText(string value)
    {
        if (!TryDecodeBase64(value, out byte[] decoded))
        {
            return false;
        }

        return decoded.Length > 0 && decoded.Length % 16 == 0;
    }

    private static bool IsValidBase64(string? value, int? expectedBytes)
    {
        if (!TryDecodeBase64(value, out byte[] decoded))
        {
            return false;
        }

        return expectedBytes is null ? decoded.Length > 0 : decoded.Length == expectedBytes.Value;
    }

    private static bool TryDecodeBase64(string? value, out byte[] decoded)
    {
        decoded = [];
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        try
        {
            decoded = Convert.FromBase64String(value);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static bool IsSafeFileName(string name) =>
        !string.IsNullOrWhiteSpace(name) &&
        name.EndsWith(".maFile", StringComparison.OrdinalIgnoreCase) &&
        name == Path.GetFileName(name) &&
        !name.Contains("..", StringComparison.Ordinal);

    private static void RestoreDestinations(
        string directory,
        string backupDirectory,
        IReadOnlyDictionary<string, bool> destinations)
    {
        foreach ((string fileName, bool existed) in destinations)
        {
            string destination = Path.Combine(directory, fileName);
            string backup = Path.Combine(backupDirectory, fileName);
            if (existed && File.Exists(backup))
            {
                File.Copy(backup, destination, overwrite: true);
            }
            else if (!existed && File.Exists(destination))
            {
                File.Delete(destination);
            }
        }
    }
}
