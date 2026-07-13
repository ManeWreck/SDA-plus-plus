using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SteamAuth;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;

namespace Steam_Desktop_Authenticator
{
    internal sealed class CloudSyncService
    {
        private const string ManifestFileName = "manifest.json";

        internal sealed class CloudPullPlan
        {
            internal Manifest RemoteManifest { get; set; }
            internal Dictionary<string, string> AccountFiles { get; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            internal string ManifestJson { get; set; }
            internal string CredentialsJson { get; set; }
            internal string CredentialsPath { get; set; }
            internal int LocalAccountCount { get; set; }

            public int RemoteAccountCount => RemoteManifest?.Entries?.Count ?? 0;
            public bool IsEncrypted => RemoteManifest?.Encrypted == true;
            public bool IncludesCredentials => CredentialsJson != null;
        }

        internal sealed class CloudPullResult
        {
            public int AccountCount { get; set; }
            public bool CredentialsRestored { get; set; }
            public string BackupDirectory { get; set; }
        }

        public async Task<string> TestConnectionAsync(ICloudStorageProvider provider)
        {
            return await provider.TestConnectionAsync();
        }

        public async Task<CloudPullPlan> PreparePullAsync(ICloudStorageProvider provider, bool syncStoredCredentials, ICloudStorageProvider credentialsProvider = null)
        {
            string maDir = Path.Combine(Manifest.GetExecutableDir(), "maFiles");
            string credentialsPath = ResolveCredentialsFilePath();
            string credentialsFileName = Path.GetFileName(credentialsPath);
            Directory.CreateDirectory(maDir);

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
                throw PullError("Cloud manifest is invalid.", "Облачный manifest повреждён или имеет неверный формат.");
            }

            var plan = new CloudPullPlan
            {
                RemoteManifest = remoteManifest,
                ManifestJson = manifestJson,
                CredentialsPath = credentialsPath,
                LocalAccountCount = Manifest.GetManifest(true).Entries?.Count ?? 0
            };

            foreach (Manifest.ManifestEntry entry in remoteManifest.Entries ?? new List<Manifest.ManifestEntry>())
            {
                ValidateRemoteFileName(entry?.Filename);
                plan.AccountFiles[entry.Filename] = await provider.DownloadTextAsync(entry.Filename);
            }

            if (syncStoredCredentials)
            {
                ICloudStorageProvider activeCredentialsProvider = credentialsProvider ?? provider;
                string credentialsJson = await activeCredentialsProvider.DownloadTextAsync(credentialsFileName, optional: true);
                if (credentialsJson != null)
                {
                    ValidateCredentialsDocument(credentialsJson);
                    plan.CredentialsJson = credentialsJson;
                }
            }

            ValidateCredentialsDestination(plan, maDir);
            ValidatePullPlan(plan);
            return plan;
        }

        public CloudPullResult CommitPull(CloudPullPlan plan)
        {
            if (plan == null)
            {
                throw new ArgumentNullException(nameof(plan));
            }

            ValidatePullPlan(plan);
            string maDir = Path.Combine(Manifest.GetExecutableDir(), "maFiles");
            Directory.CreateDirectory(maDir);
            string backupDir = CreateBackupDirectory("pull");
            string accountBackupDir = Path.Combine(backupDir, "maFiles");
            Directory.CreateDirectory(accountBackupDir);

            var destinations = plan.AccountFiles.Keys
                .Select(fileName => Path.Combine(maDir, fileName))
                .ToList();
            string manifestPath = Path.Combine(maDir, ManifestFileName);
            destinations.Add(manifestPath);
            if (plan.IncludesCredentials)
            {
                destinations.Add(plan.CredentialsPath);
            }

            var existedBefore = destinations.ToDictionary(path => path, File.Exists, StringComparer.OrdinalIgnoreCase);
            foreach (string file in Directory.GetFiles(maDir, "*.maFile"))
            {
                BackupFileIfExists(file, accountBackupDir);
            }

            BackupFileIfExists(manifestPath, accountBackupDir);
            if (plan.IncludesCredentials)
            {
                BackupFileIfExists(plan.CredentialsPath, Path.Combine(backupDir, "credentials"));
            }

            try
            {
                foreach (KeyValuePair<string, string> accountFile in plan.AccountFiles)
                {
                    WriteTextAtomically(Path.Combine(maDir, accountFile.Key), accountFile.Value);
                }

                if (plan.IncludesCredentials)
                {
                    WriteTextAtomically(plan.CredentialsPath, plan.CredentialsJson);
                }

                // The manifest is committed last so it never references files that were not written.
                WriteTextAtomically(manifestPath, plan.ManifestJson);
            }
            catch
            {
                RestoreAfterFailedCommit(destinations, existedBefore, accountBackupDir, backupDir, plan.CredentialsPath);
                throw;
            }

            return new CloudPullResult
            {
                AccountCount = plan.RemoteAccountCount,
                CredentialsRestored = plan.IncludesCredentials,
                BackupDirectory = backupDir
            };
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
                DateTime.Now.ToString("yyyyMMdd-HHmmss-fff") + "-" + action + "-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            Directory.CreateDirectory(path);
            return path;
        }

        private static void BackupFileIfExists(string sourceFile, string backupDir)
        {
            if (File.Exists(sourceFile))
            {
                Directory.CreateDirectory(backupDir);
                File.Copy(sourceFile, Path.Combine(backupDir, Path.GetFileName(sourceFile)), true);
            }
        }

        private static void ValidatePullPlan(CloudPullPlan plan)
        {
            if (plan.RemoteManifest?.Entries == null || plan.RemoteManifest.Entries.Count == 0)
            {
                throw PullError("The cloud vault contains no account entries. Nothing was changed locally.", "В облачном хранилище нет аккаунтов. Локальные данные не изменены.");
            }

            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var steamIds = new HashSet<ulong>();
            foreach (Manifest.ManifestEntry entry in plan.RemoteManifest.Entries)
            {
                ValidateRemoteFileName(entry?.Filename);
                if (!names.Add(entry.Filename))
                {
                    throw PullError("The cloud manifest contains a duplicate account file: " + entry.Filename, "Облачный manifest содержит повторяющийся файл аккаунта: " + entry.Filename);
                }

                ulong expectedSteamId = entry.SteamID != 0 ? entry.SteamID : TryParseSteamIdFromFileName(entry.Filename);
                if (expectedSteamId == 0 || !steamIds.Add(expectedSteamId))
                {
                    throw PullError("The cloud manifest contains an invalid or duplicate SteamID for " + entry.Filename + ".", "Облачный manifest содержит неверный или повторяющийся SteamID для " + entry.Filename + ".");
                }

                if (!plan.AccountFiles.TryGetValue(entry.Filename, out string contents) || string.IsNullOrWhiteSpace(contents))
                {
                    throw PullError("The cloud account file is missing or empty: " + entry.Filename, "Файл аккаунта в облаке отсутствует или пуст: " + entry.Filename);
                }

                if (plan.RemoteManifest.Encrypted)
                {
                    ValidateEncryptedAccount(entry, contents);
                }
                else
                {
                    ValidatePlainAccount(entry.Filename, expectedSteamId, contents);
                }
            }

            Manifest normalized = TryDeserializeManifest(plan.ManifestJson);
            if (normalized == null)
            {
                throw PullError("The prepared cloud manifest is invalid. Nothing was changed locally.", "Подготовленный облачный manifest повреждён. Локальные данные не изменены.");
            }
        }

        private static void ValidateRemoteFileName(string fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName)
                || !string.Equals(fileName, Path.GetFileName(fileName), StringComparison.Ordinal)
                || !string.Equals(Path.GetExtension(fileName), ".maFile", StringComparison.OrdinalIgnoreCase))
            {
                throw PullError("The cloud manifest contains an unsafe account filename.", "Облачный manifest содержит небезопасное имя файла аккаунта.");
            }
        }

        private static void ValidatePlainAccount(string fileName, ulong expectedSteamId, string contents)
        {
            try
            {
                SteamGuardAccount account = JsonConvert.DeserializeObject<SteamGuardAccount>(contents);
                if (account?.Session == null || account.Session.SteamID == 0 || account.Session.SteamID != expectedSteamId)
                {
                    throw new InvalidOperationException();
                }
            }
            catch
            {
                throw PullError("The cloud account file is invalid or does not match its SteamID: " + fileName, "Файл аккаунта повреждён или не соответствует SteamID: " + fileName);
            }
        }

        private static void ValidateEncryptedAccount(Manifest.ManifestEntry entry, string contents)
        {
            try
            {
                byte[] salt = Convert.FromBase64String(entry.Salt ?? string.Empty);
                byte[] iv = Convert.FromBase64String(entry.IV ?? string.Empty);
                byte[] cipher = Convert.FromBase64String(contents.Trim());
                bool valid = salt.Length >= 8 && iv.Length == 16 && cipher.Length > 0 && cipher.Length % 16 == 0;
                Array.Clear(salt, 0, salt.Length);
                Array.Clear(iv, 0, iv.Length);
                Array.Clear(cipher, 0, cipher.Length);
                if (!valid)
                {
                    throw new CryptographicException();
                }
            }
            catch
            {
                throw PullError("The encrypted cloud account file or its encryption metadata is invalid: " + entry.Filename, "Зашифрованный файл аккаунта или его данные шифрования повреждены: " + entry.Filename);
            }
        }

        private static void ValidateCredentialsDocument(string credentialsJson)
        {
            try
            {
                JObject document = JObject.Parse(credentialsJson);
                bool encrypted = document.Value<bool?>("encrypted") ?? false;
                if (encrypted)
                {
                    string algorithm = document.Value<string>("algorithm");
                    byte[] iv = Convert.FromBase64String(document.Value<string>("iv") ?? string.Empty);
                    byte[] payload = Convert.FromBase64String(document.Value<string>("payload") ?? string.Empty);
                    bool valid = string.Equals(algorithm, "aes-256-cbc", StringComparison.OrdinalIgnoreCase)
                        && iv.Length == 16
                        && payload.Length > 0
                        && payload.Length % 16 == 0;
                    Array.Clear(iv, 0, iv.Length);
                    Array.Clear(payload, 0, payload.Length);
                    if (!valid)
                    {
                        throw new JsonException();
                    }
                }
                else if (document["entries"] is not JArray)
                {
                    throw new JsonException();
                }
            }
            catch
            {
                throw PullError("The cloud credentials file is invalid. Nothing was changed locally.", "Облачный файл логинов повреждён. Локальные данные не изменены.");
            }
        }

        private static void ValidateCredentialsDestination(CloudPullPlan plan, string maDir)
        {
            if (!plan.IncludesCredentials)
            {
                return;
            }

            string credentialsPath = Path.GetFullPath(plan.CredentialsPath);
            string manifestPath = Path.GetFullPath(Path.Combine(maDir, ManifestFileName));
            bool collides = string.Equals(credentialsPath, manifestPath, StringComparison.OrdinalIgnoreCase)
                || plan.AccountFiles.Keys.Any(fileName => string.Equals(
                    credentialsPath,
                    Path.GetFullPath(Path.Combine(maDir, fileName)),
                    StringComparison.OrdinalIgnoreCase));
            if (collides)
            {
                throw PullError(
                    "The credentials storage path conflicts with an account or manifest file. Nothing was changed locally.",
                    "Путь файла логинов совпадает с файлом аккаунта или manifest. Локальные данные не изменены.");
            }
        }

        private static InvalidOperationException PullError(string english, string russian)
        {
            return new InvalidOperationException(Localizer.Choose(english, russian));
        }

        private static void WriteTextAtomically(string destination, string contents)
        {
            string directory = Path.GetDirectoryName(destination);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            string temporaryFile = destination + ".pull-" + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(temporaryFile, contents);
                File.Move(temporaryFile, destination, true);
            }
            finally
            {
                if (File.Exists(temporaryFile))
                {
                    File.Delete(temporaryFile);
                }
            }
        }

        private static void RestoreAfterFailedCommit(
            IEnumerable<string> destinations,
            IReadOnlyDictionary<string, bool> existedBefore,
            string accountBackupDir,
            string backupDir,
            string credentialsPath)
        {
            foreach (string destination in destinations)
            {
                string backupFile = string.Equals(destination, credentialsPath, StringComparison.OrdinalIgnoreCase)
                    ? Path.Combine(backupDir, "credentials", Path.GetFileName(destination))
                    : Path.Combine(accountBackupDir, Path.GetFileName(destination));
                if (File.Exists(backupFile))
                {
                    File.Copy(backupFile, destination, true);
                }
                else if (existedBefore.TryGetValue(destination, out bool existed) && !existed && File.Exists(destination))
                {
                    File.Delete(destination);
                }
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
