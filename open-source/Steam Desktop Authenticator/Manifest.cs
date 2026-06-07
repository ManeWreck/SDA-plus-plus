using Newtonsoft.Json;
using SteamAuth;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Steam_Desktop_Authenticator
{
    public enum CloudProvider
    {
        WebDav = 0,
        S3Compatible = 1,
        Dropbox = 2,
        OneDrivePersonal = 3,
        GoogleDrive = 4
    }

    public class Manifest
    {
        [JsonProperty("encrypted")]
        public bool Encrypted { get; set; }

        [JsonProperty("first_run")]
        public bool FirstRun { get; set; } = true;

        [JsonProperty("entries")]
        public List<ManifestEntry> Entries { get; set; }

        [JsonProperty("periodic_checking")]
        public bool PeriodicChecking { get; set; } = false;

        [JsonProperty("periodic_checking_interval")]
        public int PeriodicCheckingInterval { get; set; } = 5;

        [JsonProperty("periodic_checking_checkall")]
        public bool CheckAllAccounts { get; set; } = false;

        [JsonProperty("auto_confirm_market_transactions")]
        public bool AutoConfirmMarketTransactions { get; set; } = false;

        [JsonProperty("auto_confirm_trades")]
        public bool AutoConfirmTrades { get; set; } = false;

        [JsonProperty("qr_hotkeys_enabled")]
        public bool QrHotkeysEnabled { get; set; } = false;

        [JsonProperty("qr_hotkey_toggle")]
        public HotkeyBinding QrHotkeyToggle { get; set; } = HotkeyBindingHelper.CreateDefault(Keys.Q);

        [JsonProperty("qr_hotkey_scan")]
        public HotkeyBinding QrHotkeyScan { get; set; } = HotkeyBindingHelper.CreateDefault(Keys.S);

        [JsonProperty("account_hotkey_previous")]
        public HotkeyBinding AccountHotkeyPrevious { get; set; } = HotkeyBindingHelper.CreateDefault(Keys.Left);

        [JsonProperty("account_hotkey_next")]
        public HotkeyBinding AccountHotkeyNext { get; set; } = HotkeyBindingHelper.CreateDefault(Keys.Right);

        [JsonProperty("qr_capture_mode")]
        public QrCaptureMode QrCaptureMode { get; set; } = QrCaptureMode.FullDesktop;

        [JsonProperty("qr_cursor_scan_size")]
        public int QrCursorScanSize { get; set; } = 700;

        [JsonProperty("webdav_url")]
        public string WebDavUrl { get; set; } = string.Empty;

        [JsonProperty("webdav_username")]
        public string WebDavUsername { get; set; } = string.Empty;

        [JsonProperty("webdav_remote_path")]
        public string WebDavRemotePath { get; set; } = "SDAppVault";

        [JsonProperty("webdav_sync_credentials")]
        public bool WebDavSyncStoredCredentials { get; set; } = false;

        [JsonProperty("webdav_last_sync_action")]
        public string WebDavLastSyncAction { get; set; } = string.Empty;

        [JsonProperty("webdav_last_sync_success")]
        public bool? WebDavLastSyncSuccess { get; set; }

        [JsonProperty("webdav_last_sync_utc")]
        public DateTime? WebDavLastSyncUtc { get; set; }

        [JsonProperty("cloud_provider")]
        public CloudProvider CloudProvider { get; set; } = CloudProvider.WebDav;

        [JsonProperty("s3_endpoint")]
        public string S3Endpoint { get; set; } = string.Empty;

        [JsonProperty("s3_bucket")]
        public string S3Bucket { get; set; } = string.Empty;

        [JsonProperty("s3_access_key")]
        public string S3AccessKey { get; set; } = string.Empty;

        [JsonProperty("s3_remote_path")]
        public string S3RemotePath { get; set; } = "SDAppVault";

        [JsonProperty("dropbox_remote_path")]
        public string DropboxRemotePath { get; set; } = "SDAppVault";

        [JsonProperty("onedrive_client_id")]
        public string OneDriveClientId { get; set; } = string.Empty;

        [JsonProperty("onedrive_remote_path")]
        public string OneDriveRemotePath { get; set; } = "SDAppVault";

        [JsonProperty("gdrive_client_id")]
        public string GoogleDriveClientId { get; set; } = string.Empty;

        [JsonProperty("gdrive_remote_path")]
        public string GoogleDriveRemotePath { get; set; } = "SDAppVault";

        [JsonProperty("auto_login_for_expired_sessions")]
        public bool AutoLoginForExpiredSessions { get; set; } = true;

        [JsonProperty("ask_before_auto_login")]
        public bool AskBeforeAutoLogin { get; set; } = true;

        [JsonProperty("credentials_storage_path")]
        public string CredentialsStoragePath { get; set; } = @"maFiles\credentials.secure.json";

        [JsonProperty("credentials_require_encryption")]
        public bool CredentialsRequireEncryption { get; set; } = true;

        [JsonProperty("allow_unencrypted_local_credentials")]
        public bool AllowUnencryptedLocalCredentials { get; set; } = false;

        [JsonProperty("credentials_cloud_enabled")]
        public bool CredentialsCloudEnabled { get; set; } = false;

        [JsonProperty("credentials_cloud_provider")]
        public CloudProvider CredentialsCloudProvider { get; set; } = CloudProvider.WebDav;

        [JsonProperty("credentials_cloud_remote_path")]
        public string CredentialsCloudRemotePath { get; set; } = "SDAppCredentials";

        [JsonProperty("ui_language")]
        public AppLanguage UiLanguage { get; set; } = AppLanguage.English;

        private static Manifest _manifest { get; set; }

        public static string GetExecutableDir()
        {
            return Path.GetDirectoryName(System.Reflection.Assembly.GetEntryAssembly().Location);
        }

        public static Manifest GetManifest(bool forceLoad = false)
        {
            // Check if already staticly loaded
            if (_manifest != null && !forceLoad)
            {
                return _manifest;
            }

            // Find config dir and manifest file
            string maDir = Manifest.GetExecutableDir() + "/maFiles/";
            string manifestFile = maDir + "manifest.json";

            // If there's no config dir, create it
            if (!Directory.Exists(maDir))
            {
                _manifest = GenerateNewManifest(false);
                return _manifest;
            }

            // If there's no manifest, throw exception
            if (!File.Exists(manifestFile))
            {
                if (!DirectoryContainsAccountFiles(maDir))
                {
                    _manifest = GenerateNewManifest(false);
                    return _manifest;
                }

                throw new ManifestParseException();
            }

            try
            {
                string manifestContents = File.ReadAllText(manifestFile);
                _manifest = JsonConvert.DeserializeObject<Manifest>(manifestContents);

                if (_manifest.Encrypted && _manifest.Entries.Count == 0)
                {
                    _manifest.Encrypted = false;
                    _manifest.Save();
                }

                _manifest.RecomputeExistingEntries();
                _manifest.EnsureDefaults();

                return _manifest;
            }
            catch (Exception)
            {
                throw new ManifestParseException();
            }
        }

        public static Manifest GenerateNewManifest(bool scanDir = false)
        {
            // No directory means no manifest file anyways.
            Manifest newManifest = new Manifest();
            newManifest.Encrypted = false;
            newManifest.PeriodicCheckingInterval = 5;
            newManifest.PeriodicChecking = false;
            newManifest.AutoConfirmMarketTransactions = false;
            newManifest.AutoConfirmTrades = false;
            newManifest.QrHotkeysEnabled = false;
            newManifest.QrHotkeyToggle = HotkeyBindingHelper.CreateDefault(Keys.Q);
            newManifest.QrHotkeyScan = HotkeyBindingHelper.CreateDefault(Keys.S);
            newManifest.AccountHotkeyPrevious = HotkeyBindingHelper.CreateDefault(Keys.Left);
            newManifest.AccountHotkeyNext = HotkeyBindingHelper.CreateDefault(Keys.Right);
            newManifest.QrCaptureMode = QrCaptureMode.FullDesktop;
            newManifest.QrCursorScanSize = 700;
            newManifest.WebDavUrl = string.Empty;
            newManifest.WebDavUsername = string.Empty;
            newManifest.WebDavRemotePath = "SDAppVault";
            newManifest.WebDavSyncStoredCredentials = false;
            newManifest.WebDavLastSyncAction = string.Empty;
            newManifest.WebDavLastSyncSuccess = null;
            newManifest.WebDavLastSyncUtc = null;
            newManifest.CloudProvider = CloudProvider.WebDav;
            newManifest.S3Endpoint = string.Empty;
            newManifest.S3Bucket = string.Empty;
            newManifest.S3AccessKey = string.Empty;
            newManifest.S3RemotePath = "SDAppVault";
            newManifest.DropboxRemotePath = "SDAppVault";
            newManifest.OneDriveClientId = string.Empty;
            newManifest.OneDriveRemotePath = "SDAppVault";
            newManifest.GoogleDriveClientId = string.Empty;
            newManifest.GoogleDriveRemotePath = "SDAppVault";
            newManifest.AutoLoginForExpiredSessions = true;
            newManifest.AskBeforeAutoLogin = true;
            newManifest.CredentialsStoragePath = @"maFiles\credentials.secure.json";
            newManifest.CredentialsRequireEncryption = true;
            newManifest.AllowUnencryptedLocalCredentials = false;
            newManifest.CredentialsCloudEnabled = false;
            newManifest.CredentialsCloudProvider = CloudProvider.WebDav;
            newManifest.CredentialsCloudRemotePath = "SDAppCredentials";
            newManifest.UiLanguage = AppLanguage.English;
            newManifest.Entries = new List<ManifestEntry>();
            newManifest.FirstRun = true;

            // Take a pre-manifest version and generate a manifest for it.
            if (scanDir)
            {
                string maDir = Manifest.GetExecutableDir() + "/maFiles/";
                if (Directory.Exists(maDir))
                {
                    DirectoryInfo dir = new DirectoryInfo(maDir);
                    var files = dir.GetFiles();

                    foreach (var file in files)
                    {
                        if (file.Extension != ".maFile") continue;

                        string contents = File.ReadAllText(file.FullName);
                        try
                        {
                            SteamGuardAccount account = JsonConvert.DeserializeObject<SteamGuardAccount>(contents);
                            ManifestEntry newEntry = new ManifestEntry()
                            {
                                Filename = file.Name,
                                SteamID = account.Session.SteamID
                            };
                            newManifest.Entries.Add(newEntry);
                        }
                        catch (Exception)
                        {
                            throw new MaFileEncryptedException();
                        }
                    }

                    if (newManifest.Entries.Count > 0)
                    {
                        newManifest.Save();
                        newManifest.PromptSetupPassKey("Эта версия SDA поддерживает шифрование. Введите ключ шифрования ниже или нажмите отмену, чтобы оставить данные без шифрования.");
                    }
                }
            }

            if (newManifest.Save())
            {
                return newManifest;
            }

            return null;
        }

        private static bool DirectoryContainsAccountFiles(string maDir)
        {
            if (!Directory.Exists(maDir))
            {
                return false;
            }

            return Directory.EnumerateFiles(maDir, "*.maFile", SearchOption.TopDirectoryOnly).Any();
        }

        public class IncorrectPassKeyException : Exception { }
        public class ManifestNotEncryptedException : Exception { }

        public string PromptForPassKey()
        {
            if (!this.Encrypted)
            {
                throw new ManifestNotEncryptedException();
            }

            bool passKeyValid = false;
            string passKey = null;
            while (!passKeyValid)
            {
                InputForm passKeyForm = new InputForm("Введите ключ шифрования.", true);
                passKeyForm.ShowDialog();
                if (!passKeyForm.Canceled)
                {
                    passKey = passKeyForm.txtBox.Text;
                    passKeyValid = this.VerifyPasskey(passKey);
                    if (!passKeyValid)
                    {
                        MessageBox.Show("Неверный ключ шифрования.");
                    }
                }
                else
                {
                    return null;
                }
            }
            return passKey;
        }

        public string PromptSetupPassKey(string initialPrompt = "Введите ключ шифрования или нажмите отмену, чтобы не использовать шифрование.")
        {
            InputForm newPassKeyForm = new InputForm(initialPrompt);
            newPassKeyForm.ShowDialog();
            if (newPassKeyForm.Canceled || newPassKeyForm.txtBox.Text.Length == 0)
            {
                MessageBox.Show("ВНИМАНИЕ: вы решили не шифровать файлы. Это создает серьезный риск безопасности. Если кто-то получит доступ к вашему компьютеру, он сможет лишить вас доступа к аккаунту и украсть предметы.");
                return null;
            }

            InputForm newPassKeyForm2 = new InputForm("Подтвердите новый ключ шифрования.");
            newPassKeyForm2.ShowDialog();
            if (newPassKeyForm2.Canceled)
            {
                MessageBox.Show("ВНИМАНИЕ: вы решили не шифровать файлы. Это создает серьезный риск безопасности. Если кто-то получит доступ к вашему компьютеру, он сможет лишить вас доступа к аккаунту и украсть предметы.");
                return null;
            }

            string newPassKey = newPassKeyForm.txtBox.Text;
            string confirmPassKey = newPassKeyForm2.txtBox.Text;

            if (newPassKey != confirmPassKey)
            {
                MessageBox.Show("Ключи шифрования не совпадают.");
                return null;
            }

            if (!this.ChangeEncryptionKey(null, newPassKey))
            {
                MessageBox.Show("Не удалось установить ключ шифрования.");
                return null;
            }
            else
            {
                MessageBox.Show("Ключ шифрования успешно установлен.");
            }

            return newPassKey;
        }

        public SteamAuth.SteamGuardAccount[] GetAllAccounts(string passKey = null, int limit = -1)
        {
            if (passKey == null && this.Encrypted) return new SteamGuardAccount[0];
            string maDir = Manifest.GetExecutableDir() + "/maFiles/";

            List<SteamAuth.SteamGuardAccount> accounts = new List<SteamAuth.SteamGuardAccount>();
            foreach (var entry in this.Entries)
            {
                string fileText = File.ReadAllText(maDir + entry.Filename);
                if (this.Encrypted)
                {
                    string decryptedText = FileEncryptor.DecryptData(passKey, entry.Salt, entry.IV, fileText);
                    if (decryptedText == null) return new SteamGuardAccount[0];
                    fileText = decryptedText;
                }

                var account = JsonConvert.DeserializeObject<SteamAuth.SteamGuardAccount>(fileText);
                if (account == null) continue;
                accounts.Add(account);

                if (limit != -1 && limit >= accounts.Count)
                    break;
            }

            return accounts.ToArray();
        }

        public bool ChangeEncryptionKey(string oldKey, string newKey)
        {
            if (this.Encrypted)
            {
                if (!this.VerifyPasskey(oldKey))
                {
                    return false;
                }
            }
            bool toEncrypt = newKey != null;

            string maDir = Manifest.GetExecutableDir() + "/maFiles/";
            for (int i = 0; i < this.Entries.Count; i++)
            {
                ManifestEntry entry = this.Entries[i];
                string filename = maDir + entry.Filename;
                if (!File.Exists(filename)) continue;

                string fileContents = File.ReadAllText(filename);
                if (this.Encrypted)
                {
                    fileContents = FileEncryptor.DecryptData(oldKey, entry.Salt, entry.IV, fileContents);
                }

                string newSalt = null;
                string newIV = null;
                string toWriteFileContents = fileContents;

                if (toEncrypt)
                {
                    newSalt = FileEncryptor.GetRandomSalt();
                    newIV = FileEncryptor.GetInitializationVector();
                    toWriteFileContents = FileEncryptor.EncryptData(newKey, newSalt, newIV, fileContents);
                }

                File.WriteAllText(filename, toWriteFileContents);
                entry.IV = newIV;
                entry.Salt = newSalt;
            }

            this.Encrypted = toEncrypt;

            this.Save();
            return true;
        }

        public bool VerifyPasskey(string passkey)
        {
            if (!this.Encrypted || this.Entries.Count == 0) return true;

            var accounts = this.GetAllAccounts(passkey, 1);
            return accounts != null && accounts.Length == 1;
        }

        public bool RemoveAccount(SteamGuardAccount account, bool deleteMaFile = true)
        {
            ManifestEntry entry = (from e in this.Entries where e.SteamID == account.Session.SteamID select e).FirstOrDefault();
            if (entry == null) return true; // If something never existed, did you do what they asked?

            string maDir = Manifest.GetExecutableDir() + "/maFiles/";
            string filename = maDir + entry.Filename;
            this.Entries.Remove(entry);

            if (this.Entries.Count == 0)
            {
                this.Encrypted = false;
            }

            if (this.Save() && deleteMaFile)
            {
                try
                {
                    File.Delete(filename);
                    return true;
                }
                catch (Exception)
                {
                    return false;
                }
            }

            return false;
        }

        public bool SaveAccount(SteamGuardAccount account, bool encrypt, string passKey = null)
        {
            if (encrypt && String.IsNullOrEmpty(passKey)) return false;
            if (!encrypt && this.Encrypted) return false;

            string salt = null;
            string iV = null;
            string jsonAccount = JsonConvert.SerializeObject(account);

            if (encrypt)
            {
                salt = FileEncryptor.GetRandomSalt();
                iV = FileEncryptor.GetInitializationVector();
                string encrypted = FileEncryptor.EncryptData(passKey, salt, iV, jsonAccount);
                if (encrypted == null) return false;
                jsonAccount = encrypted;
            }

            string maDir = Manifest.GetExecutableDir() + "/maFiles/";
            string filename = account.Session.SteamID.ToString() + ".maFile";

            ManifestEntry newEntry = new ManifestEntry()
            {
                SteamID = account.Session.SteamID,
                IV = iV,
                Salt = salt,
                Filename = filename
            };

            bool foundExistingEntry = false;
            for (int i = 0; i < this.Entries.Count; i++)
            {
                if (this.Entries[i].SteamID == account.Session.SteamID)
                {
                    this.Entries[i] = newEntry;
                    foundExistingEntry = true;
                    break;
                }
            }

            if (!foundExistingEntry)
            {
                this.Entries.Add(newEntry);
            }

            bool wasEncrypted = this.Encrypted;
            this.Encrypted = encrypt || this.Encrypted;

            if (!this.Save())
            {
                this.Encrypted = wasEncrypted;
                return false;
            }

            try
            {
                File.WriteAllText(maDir + filename, jsonAccount);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        public bool Save()
        {
            string maDir = Manifest.GetExecutableDir() + "/maFiles/";
            string filename = maDir + "manifest.json";
            if (!Directory.Exists(maDir))
            {
                try
                {
                    Directory.CreateDirectory(maDir);
                }
                catch (Exception)
                {
                    return false;
                }
            }

            try
            {
                string contents = JsonConvert.SerializeObject(this);
                File.WriteAllText(filename, contents);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private void RecomputeExistingEntries()
        {
            List<ManifestEntry> newEntries = new List<ManifestEntry>();
            string maDir = Manifest.GetExecutableDir() + "/maFiles/";

            foreach (var entry in this.Entries)
            {
                string filename = maDir + entry.Filename;
                if (File.Exists(filename))
                {
                    newEntries.Add(entry);
                }
            }

            this.Entries = newEntries;

            if (this.Entries.Count == 0)
            {
                this.Encrypted = false;
            }
        }

        private void EnsureDefaults()
        {
            QrHotkeyToggle = HotkeyBindingHelper.Normalize(QrHotkeyToggle, Keys.Q);
            QrHotkeyScan = HotkeyBindingHelper.Normalize(QrHotkeyScan, Keys.S);
            AccountHotkeyPrevious = HotkeyBindingHelper.Normalize(AccountHotkeyPrevious, Keys.Left);
            AccountHotkeyNext = HotkeyBindingHelper.Normalize(AccountHotkeyNext, Keys.Right);

            if (!Enum.IsDefined(typeof(QrCaptureMode), QrCaptureMode))
            {
                QrCaptureMode = QrCaptureMode.FullDesktop;
            }

            if (QrCursorScanSize < 250)
            {
                QrCursorScanSize = 250;
            }

            WebDavUrl ??= string.Empty;
            WebDavUsername ??= string.Empty;
            WebDavRemotePath = string.IsNullOrWhiteSpace(WebDavRemotePath) ? "SDAppVault" : WebDavRemotePath.Trim();
            WebDavLastSyncAction ??= string.Empty;
            S3Endpoint ??= string.Empty;
            S3Bucket ??= string.Empty;
            S3AccessKey ??= string.Empty;
            S3RemotePath = string.IsNullOrWhiteSpace(S3RemotePath) ? "SDAppVault" : S3RemotePath.Trim();
            DropboxRemotePath = string.IsNullOrWhiteSpace(DropboxRemotePath) ? "SDAppVault" : DropboxRemotePath.Trim();
            OneDriveClientId ??= string.Empty;
            OneDriveRemotePath = string.IsNullOrWhiteSpace(OneDriveRemotePath) ? "SDAppVault" : OneDriveRemotePath.Trim();
            GoogleDriveClientId ??= string.Empty;
            GoogleDriveRemotePath = string.IsNullOrWhiteSpace(GoogleDriveRemotePath) ? "SDAppVault" : GoogleDriveRemotePath.Trim();
            CredentialsStoragePath = string.IsNullOrWhiteSpace(CredentialsStoragePath) ? @"maFiles\credentials.secure.json" : CredentialsStoragePath.Trim();
            CredentialsCloudRemotePath = string.IsNullOrWhiteSpace(CredentialsCloudRemotePath) ? "SDAppCredentials" : CredentialsCloudRemotePath.Trim();

            if (!Enum.IsDefined(typeof(CloudProvider), CloudProvider))
            {
                CloudProvider = CloudProvider.WebDav;
            }

            if (!Enum.IsDefined(typeof(CloudProvider), CredentialsCloudProvider))
            {
                CredentialsCloudProvider = CloudProvider.WebDav;
            }

            if (!System.Enum.IsDefined(typeof(AppLanguage), UiLanguage))
            {
                UiLanguage = AppLanguage.English;
            }
        }

        public void MoveEntry(int from, int to)
        {
            if (from < 0 || to < 0 || from > Entries.Count || to > Entries.Count - 1) return;
            ManifestEntry sel = Entries[from];
            Entries.RemoveAt(from);
            Entries.Insert(to, sel);
            Save();
        }

        public bool IsFavorite(ulong steamId)
        {
            ManifestEntry entry = Entries.FirstOrDefault(e => e.SteamID == steamId);
            return entry != null && entry.Favorite;
        }

        public void SetFavorite(ulong steamId, bool favorite)
        {
            ManifestEntry entry = Entries.FirstOrDefault(e => e.SteamID == steamId);
            if (entry == null || entry.Favorite == favorite)
            {
                return;
            }

            entry.Favorite = favorite;
            Save();
        }

        public class ManifestEntry
        {
            [JsonProperty("encryption_iv")]
            public string IV { get; set; }

            [JsonProperty("encryption_salt")]
            public string Salt { get; set; }

            [JsonProperty("filename")]
            public string Filename { get; set; }

            [JsonProperty("steamid")]
            public ulong SteamID { get; set; }

            [JsonProperty("favorite")]
            public bool Favorite { get; set; }
        }
    }
}
