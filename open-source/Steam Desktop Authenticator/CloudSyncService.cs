using Newtonsoft.Json;
using SteamAuth;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Steam_Desktop_Authenticator
{
    internal sealed class CloudSyncService
    {
        private const string ManifestFileName = "manifest.json";
        public async Task<string> TestConnectionAsync(ICloudStorageProvider provider)
        {
            return await provider.TestConnectionAsync();
        }

        public async Task PullAsync(ICloudStorageProvider provider, bool syncStoredCredentials, ICloudStorageProvider credentialsProvider = null)
        {
            string maDir = Path.Combine(Manifest.GetExecutableDir(), "maFiles");
            string credentialsPath = ResolveCredentialsFilePath();
            string credentialsFileName = Path.GetFileName(credentialsPath);
            Directory.CreateDirectory(maDir);
            string backupDir = CreateBackupDirectory("pull");
            BackupFileIfExists(Path.Combine(maDir, ManifestFileName), backupDir);
            foreach (string file in Directory.GetFiles(maDir, "*.maFile"))
            {
                BackupFileIfExists(file, backupDir);
            }

            if (syncStoredCredentials)
            {
                BackupFileIfExists(credentialsPath, backupDir);
            }

            string manifestJson = await provider.DownloadTextAsync(ManifestFileName, optional: true);
            Manifest remoteManifest = TryDeserializeManifest(manifestJson);
            if (NeedsManifestEntryRecovery(remoteManifest) && provider is ICloudStorageFileListProvider listingProvider)
            {
                IReadOnlyCollection<string> remoteMaFiles = await listingProvider.ListFileNamesAsync(".maFile");
                if (remoteMaFiles.Count > 0)
                {
                    remoteManifest ??= CloneLocalManifestFallback();
                    remoteManifest.FirstRun = false;
                    remoteManifest.Entries = remoteMaFiles
                        .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                        .Select(fileName => new Manifest.ManifestEntry
                        {
                            Filename = fileName,
                            SteamID = TryParseSteamIdFromFileName(fileName)
                        })
                        .ToList();
                    manifestJson = JsonConvert.SerializeObject(remoteManifest, Formatting.Indented);
                }
            }

            if (remoteManifest == null)
            {
                throw new InvalidOperationException("Cloud manifest is invalid.");
            }

            var downloadedFiles = new Dictionary<string, string>
            {
                [ManifestFileName] = manifestJson
            };
            foreach (Manifest.ManifestEntry entry in remoteManifest.Entries ?? new List<Manifest.ManifestEntry>())
            {
                downloadedFiles[entry.Filename] = await provider.DownloadTextAsync(entry.Filename);
            }

            if (syncStoredCredentials)
            {
                ICloudStorageProvider activeCredentialsProvider = credentialsProvider ?? provider;
                string credentialsJson = await activeCredentialsProvider.DownloadTextAsync(credentialsFileName, optional: true);
                if (credentialsJson != null)
                {
                    downloadedFiles[credentialsFileName] = credentialsJson;
                }
            }

            foreach (KeyValuePair<string, string> file in downloadedFiles)
            {
                string destination = string.Equals(file.Key, credentialsFileName, StringComparison.OrdinalIgnoreCase)
                    ? credentialsPath
                    : Path.Combine(maDir, file.Key);
                string destinationDirectory = Path.GetDirectoryName(destination);
                if (!string.IsNullOrWhiteSpace(destinationDirectory))
                {
                    Directory.CreateDirectory(destinationDirectory);
                }

                File.WriteAllText(destination, file.Value);
            }

            RepairManifestEntriesIfNeeded(maDir);
        }

        public async Task PushAsync(ICloudStorageProvider provider, bool syncStoredCredentials, ICloudStorageProvider credentialsProvider = null)
        {
            string maDir = Path.Combine(Manifest.GetExecutableDir(), "maFiles");
            string credentialsPath = ResolveCredentialsFilePath();
            string credentialsFileName = Path.GetFileName(credentialsPath);
            string manifestPath = Path.Combine(maDir, ManifestFileName);
            if (!File.Exists(manifestPath))
            {
                throw new FileNotFoundException("Local manifest.json was not found.", manifestPath);
            }

            string manifestJson = File.ReadAllText(manifestPath);
            Manifest localManifest = JsonConvert.DeserializeObject<Manifest>(manifestJson);
            if (localManifest == null)
            {
                throw new InvalidOperationException("Local manifest is invalid.");
            }

            await provider.EnsureContainerAsync();
            foreach (Manifest.ManifestEntry entry in localManifest.Entries ?? new List<Manifest.ManifestEntry>())
            {
                string filePath = Path.Combine(maDir, entry.Filename);
                if (File.Exists(filePath))
                {
                    await provider.UploadTextAsync(entry.Filename, File.ReadAllText(filePath));
                }
            }

            if (syncStoredCredentials)
            {
                if (File.Exists(credentialsPath))
                {
                    ICloudStorageProvider activeCredentialsProvider = credentialsProvider ?? provider;
                    if (!ReferenceEquals(activeCredentialsProvider, provider))
                    {
                        await activeCredentialsProvider.EnsureContainerAsync();
                    }

                    await activeCredentialsProvider.UploadTextAsync(credentialsFileName, File.ReadAllText(credentialsPath));
                }
            }

            // Upload the manifest last so readers never observe references to missing files.
            await provider.UploadTextAsync(ManifestFileName, manifestJson);
        }

        private static string CreateBackupDirectory(string action)
        {
            string path = Path.Combine(
                Manifest.GetExecutableDir(),
                "maFiles",
                "backups",
                "cloudsync",
                DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + action);
            Directory.CreateDirectory(path);
            return path;
        }

        private static void BackupFileIfExists(string sourceFile, string backupDir)
        {
            if (File.Exists(sourceFile))
            {
                File.Copy(sourceFile, Path.Combine(backupDir, Path.GetFileName(sourceFile)), true);
            }
        }

        private static string ResolveCredentialsFilePath()
        {
            string configured = Manifest.GetManifest(true).CredentialsStoragePath;
            if (string.IsNullOrWhiteSpace(configured))
            {
                configured = @"maFiles\credentials.secure.json";
            }

            return Path.IsPathRooted(configured)
                ? configured
                : Path.GetFullPath(Path.Combine(Manifest.GetExecutableDir(), configured));
        }

        private static void RepairManifestEntriesIfNeeded(string maDir)
        {
            string manifestPath = Path.Combine(maDir, ManifestFileName);
            if (!File.Exists(manifestPath))
            {
                return;
            }

            Manifest manifest = JsonConvert.DeserializeObject<Manifest>(File.ReadAllText(manifestPath));
            if (manifest == null)
            {
                return;
            }

            bool needsRepair = manifest.Entries == null
                || manifest.Entries.Count == 0
                || manifest.Entries.Any(entry => entry == null
                    || string.IsNullOrWhiteSpace(entry.Filename)
                    || entry.SteamID == 0UL);
            if (!needsRepair)
            {
                return;
            }

            string[] maFiles = Directory.GetFiles(maDir, "*.maFile");
            if (maFiles.Length == 0)
            {
                return;
            }

            List<Manifest.ManifestEntry> rebuiltEntries = new List<Manifest.ManifestEntry>();
            foreach (string filePath in maFiles.OrderBy(Path.GetFileName))
            {
                string fileName = Path.GetFileName(filePath);
                ulong steamId = TryReadSteamId(filePath, fileName);
                rebuiltEntries.Add(new Manifest.ManifestEntry
                {
                    Filename = fileName,
                    SteamID = steamId
                });
            }

            manifest.Entries = rebuiltEntries;
            File.WriteAllText(manifestPath, JsonConvert.SerializeObject(manifest));
        }

        private static bool NeedsManifestEntryRecovery(Manifest manifest)
        {
            return manifest == null || manifest.Entries == null || manifest.Entries.Count == 0;
        }

        private static Manifest TryDeserializeManifest(string manifestJson)
        {
            if (string.IsNullOrWhiteSpace(manifestJson))
            {
                return null;
            }

            try
            {
                return JsonConvert.DeserializeObject<Manifest>(manifestJson);
            }
            catch
            {
                return null;
            }
        }

        private static Manifest CloneLocalManifestFallback()
        {
            try
            {
                string json = JsonConvert.SerializeObject(Manifest.GetManifest(true));
                Manifest manifest = JsonConvert.DeserializeObject<Manifest>(json);
                if (manifest != null)
                {
                    manifest.Entries = new List<Manifest.ManifestEntry>();
                    return manifest;
                }
            }
            catch
            {
            }

            return new Manifest
            {
                Entries = new List<Manifest.ManifestEntry>(),
                FirstRun = false
            };
        }

        private static ulong TryParseSteamIdFromFileName(string fileName)
        {
            string stem = Path.GetFileNameWithoutExtension(fileName);
            return ulong.TryParse(stem, out ulong parsed) ? parsed : 0UL;
        }

        private static ulong TryReadSteamId(string filePath, string fileName)
        {
            try
            {
                SteamGuardAccount account = JsonConvert.DeserializeObject<SteamGuardAccount>(File.ReadAllText(filePath));
                if (account?.Session?.SteamID > 0)
                {
                    return account.Session.SteamID;
                }
            }
            catch
            {
            }

            string stem = Path.GetFileNameWithoutExtension(fileName);
            return ulong.TryParse(stem, out ulong parsed) ? parsed : 0UL;
        }
    }
}
