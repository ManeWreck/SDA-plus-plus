using Newtonsoft.Json;
using SDAPlusPlus.Core.Vault;

namespace SDAPlusPlus.Core.Cloud;

public sealed class CloudPushPackageFactory
{
    private const int MaximumAccounts = 500;
    private const int MaximumDocumentCharacters = 2 * 1024 * 1024;

    public CloudPushPackageResult Create(string maFilesDirectory)
    {
        string directory;
        try { directory = Path.GetFullPath(maFilesDirectory); }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        { return new CloudPushPackageResult(false, "The local vault path is invalid."); }

        string manifestPath = Path.Combine(directory, "manifest.json");
        if (!File.Exists(manifestPath)) return new CloudPushPackageResult(false, "Local manifest.json was not found.");
        try
        {
            string manifestJson = File.ReadAllText(manifestPath);
            if (manifestJson.Length == 0 || manifestJson.Length > MaximumDocumentCharacters)
                return new CloudPushPackageResult(false, "Local manifest.json is empty or too large.");
            LegacyVaultManifest manifest = JsonConvert.DeserializeObject<LegacyVaultManifest>(manifestJson)
                ?? throw new JsonException("manifest.json is empty.");
            if (!manifest.Encrypted)
                return new CloudPushPackageResult(false, "Cloud upload requires an encrypted local vault. Enable vault encryption first.");
            if (manifest.Entries.Count == 0 || manifest.Entries.Count > MaximumAccounts)
                return new CloudPushPackageResult(false, "The local vault contains no accounts or exceeds the cloud account limit.");

            Dictionary<string, string> files = new(StringComparer.OrdinalIgnoreCase);
            foreach (LegacyVaultEntry entry in manifest.Entries)
            {
                if (!IsSafe(entry.Filename) || entry.SteamId == 0 || string.IsNullOrWhiteSpace(entry.Iv) || string.IsNullOrWhiteSpace(entry.Salt))
                    return new CloudPushPackageResult(false, "The local manifest contains an invalid account entry.");
                string path = Path.Combine(directory, entry.Filename);
                if (!File.Exists(path)) return new CloudPushPackageResult(false, "Local account file is missing: " + entry.Filename);
                string contents = File.ReadAllText(path);
                if (contents.Length == 0 || contents.Length > MaximumDocumentCharacters)
                    return new CloudPushPackageResult(false, entry.Filename + " is empty or too large.");
                byte[] cipher;
                try { cipher = Convert.FromBase64String(contents.Trim()); }
                catch (FormatException) { return new CloudPushPackageResult(false, entry.Filename + " is not encrypted correctly."); }
                bool valid = cipher.Length > 0 && cipher.Length % 16 == 0;
                System.Security.Cryptography.CryptographicOperations.ZeroMemory(cipher);
                if (!valid) return new CloudPushPackageResult(false, entry.Filename + " has invalid encrypted content.");
                if (!files.TryAdd(entry.Filename, contents)) return new CloudPushPackageResult(false, "The local manifest contains duplicate files.");
            }
            return new CloudPushPackageResult(true, "Encrypted vault is ready for upload.", new CloudPushPackage(manifestJson, files, true));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return new CloudPushPackageResult(false, "The local vault could not be prepared: " + ex.Message);
        }
    }

    private static bool IsSafe(string value) => !string.IsNullOrWhiteSpace(value) && value == Path.GetFileName(value) &&
        value.EndsWith(".maFile", StringComparison.OrdinalIgnoreCase) && !value.Contains("..", StringComparison.Ordinal);
}
