using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SDAPlusPlus.Core.Cloud;

namespace SDAPlusPlus.Core.Vault;

public static class LocalVaultFolderImport
{
    public static CloudPullPackageResult Prepare(string selectedDirectory)
    {
        try
        {
            string directory = Path.GetFullPath(selectedDirectory);
            if (Directory.Exists(Path.Combine(directory, "maFiles"))) directory = Path.Combine(directory, "maFiles");
            string manifestPath = Path.Combine(directory, "manifest.json");
            if (!File.Exists(manifestPath)) return new(false, "Select an SDA folder containing maFiles/manifest.json or manifest.json.");
            var manifest = JObject.Parse(File.ReadAllText(manifestPath));
            if (manifest["entries"] is not JArray entries || entries.Count == 0)
                return new(false, "The selected vault has no account entries.");
            var files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in entries)
            {
                string name = entry.Value<string>("filename") ?? string.Empty;
                if (string.IsNullOrWhiteSpace(name) || name != Path.GetFileName(name) || name.Contains(':') ||
                    name.Contains('/') || name.Contains('\\') || !name.EndsWith(".maFile", StringComparison.OrdinalIgnoreCase) || files.ContainsKey(name))
                    return new(false, "The manifest contains an unsafe or duplicate account filename.");
                string path = Path.Combine(directory, name);
                if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                    return new(false, "Linked account files are not supported.");
                files.Add(name, File.ReadAllText(path));
            }
            return new(true, "Local vault prepared.", new(manifest.ToString(), files, manifest.Value<bool?>("encrypted") ?? false));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or ArgumentException or InvalidOperationException)
        {
            return new(false, "Could not read the local vault: " + ex.Message);
        }
    }
}
