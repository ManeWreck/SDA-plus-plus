using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Steam_Desktop_Authenticator
{
    public partial class SettingsForm : Form
    {
        private readonly CloudSyncService cloudSyncService = new CloudSyncService();
        private readonly CloudSecretStore cloudSecretStore = new CloudSecretStore();
        private readonly WebDavSecretStore legacyWebDavSecretStore = new WebDavSecretStore();
        private readonly Label lblCloudProvider = new Label();
        private readonly ComboBox cmbCloudProvider = new ComboBox();
        private readonly Label lblCloudExtra = new Label();
        private readonly TextBox txtCloudExtra = new TextBox();
        private readonly Button btnOneDriveSignIn = new Button();
        private readonly Dictionary<CloudProvider, CloudProviderDraft> cloudDrafts = new Dictionary<CloudProvider, CloudProviderDraft>();
        private readonly GroupBox groupCredentials = new GroupBox();
        private readonly CheckBox chkAutoLoginExpired = new CheckBox();
        private readonly CheckBox chkAskBeforeAutoLogin = new CheckBox();
        private readonly Label lblCredentialsPath = new Label();
        private readonly TextBox txtCredentialsPath = new TextBox();
        private readonly Button btnBrowseCredentialsPath = new Button();
        private readonly CheckBox chkRequireCredentialsEncryption = new CheckBox();
        private readonly CheckBox chkAllowPlainCredentials = new CheckBox();
        private readonly CheckBox chkCredentialsCloudEnabled = new CheckBox();
        private readonly Label lblCredentialsCloudProvider = new Label();
        private readonly ComboBox cmbCredentialsCloudProvider = new ComboBox();
        private readonly Label lblCredentialsCloudPath = new Label();
        private readonly TextBox txtCredentialsCloudPath = new TextBox();
        private Manifest manifest;
        private bool fullyLoaded = false;
        private bool compactLayoutApplied = false;
        private CloudProvider? activeCloudProvider;

        private sealed class CloudProviderDraft
        {
            public string Field1 { get; set; } = string.Empty;
            public string Field2 { get; set; } = string.Empty;
            public string Field3 { get; set; } = string.Empty;
            public string Extra { get; set; } = string.Empty;
            public string RemotePath { get; set; } = "SDAppVault";
        }

        public SettingsForm()
        {
            InitializeComponent();
            Icon = Branding.LoadAppIcon();
            CreateCloudProviderControls();
            CreateCredentialsControls();
            Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
            DoubleBuffered = true;
            ModernUi.AttachWindowChrome(this, false, false);
            ModernUi.ShiftControlsDown(this, ModernUi.HeaderHeight - 4);
            ApplyCompactLayout();
            ApplyTheme();

            manifest = Manifest.GetManifest(true);
            Localizer.SetLanguage(manifest.UiLanguage);

            chkPeriodicChecking.Checked = manifest.PeriodicChecking;
            numPeriodicInterval.Value = manifest.PeriodicCheckingInterval;
            chkCheckAll.Checked = manifest.CheckAllAccounts;
            chkConfirmMarket.Checked = manifest.AutoConfirmMarketTransactions;
            chkConfirmTrades.Checked = manifest.AutoConfirmTrades;
            chkEnableQrHotkeys.Checked = manifest.QrHotkeysEnabled;
            chkSyncStoredCredentials.Checked = manifest.WebDavSyncStoredCredentials;
            chkAutoLoginExpired.Checked = manifest.AutoLoginForExpiredSessions;
            chkAskBeforeAutoLogin.Checked = manifest.AskBeforeAutoLogin;
            txtCredentialsPath.Text = manifest.CredentialsStoragePath;
            chkRequireCredentialsEncryption.Checked = manifest.CredentialsRequireEncryption;
            chkAllowPlainCredentials.Checked = manifest.AllowUnencryptedLocalCredentials;
            chkCredentialsCloudEnabled.Checked = manifest.CredentialsCloudEnabled;
            txtCredentialsCloudPath.Text = manifest.CredentialsCloudRemotePath;
            InitializeCloudDrafts();
            ReloadCloudProviders();
            cmbCloudProvider.SelectedIndex = (int)manifest.CloudProvider;
            ReloadCredentialsCloudProviders();
            cmbCredentialsCloudProvider.SelectedIndex = (int)manifest.CredentialsCloudProvider;

            cmbLanguage.Items.Clear();
            cmbLanguage.Items.Add(Localizer.LanguageDisplayName(AppLanguage.English));
            cmbLanguage.Items.Add(Localizer.LanguageDisplayName(AppLanguage.Russian));
            cmbLanguage.SelectedIndex = (int)manifest.UiLanguage;

            ReloadCaptureModes();
            cmbQrCaptureMode.SelectedIndex = (int)manifest.QrCaptureMode;
            numCursorScanSize.Value = manifest.QrCursorScanSize;

            BindHotkeyBox(txtHotkeyToggle, manifest.QrHotkeyToggle);
            BindHotkeyBox(txtHotkeyScan, manifest.QrHotkeyScan);
            BindHotkeyBox(txtHotkeyPrev, manifest.AccountHotkeyPrevious);
            BindHotkeyBox(txtHotkeyNext, manifest.AccountHotkeyNext);

            ApplyLocalization();
            RefreshCloudLastSyncInfo();
            SetControlsEnabledState(chkPeriodicChecking.Checked);
            UpdateQrControls();
            fullyLoaded = true;
        }

        private void CreateCloudProviderControls()
        {
            cmbCloudProvider.DropDownStyle = ComboBoxStyle.DropDownList;
            cmbCloudProvider.SelectedIndexChanged += cmbCloudProvider_SelectedIndexChanged;
            btnOneDriveSignIn.Click += btnOneDriveSignIn_Click;
            groupCloud.Controls.Add(lblCloudProvider);
            groupCloud.Controls.Add(cmbCloudProvider);
            groupCloud.Controls.Add(lblCloudExtra);
            groupCloud.Controls.Add(txtCloudExtra);
            groupCloud.Controls.Add(btnOneDriveSignIn);
        }

        private void CreateCredentialsControls()
        {
            cmbCredentialsCloudProvider.DropDownStyle = ComboBoxStyle.DropDownList;
            btnBrowseCredentialsPath.Click += btnBrowseCredentialsPath_Click;
            Controls.Add(groupCredentials);
            groupCredentials.Controls.Add(chkAutoLoginExpired);
            groupCredentials.Controls.Add(chkAskBeforeAutoLogin);
            groupCredentials.Controls.Add(lblCredentialsPath);
            groupCredentials.Controls.Add(txtCredentialsPath);
            groupCredentials.Controls.Add(btnBrowseCredentialsPath);
            groupCredentials.Controls.Add(chkRequireCredentialsEncryption);
            groupCredentials.Controls.Add(chkAllowPlainCredentials);
            groupCredentials.Controls.Add(chkCredentialsCloudEnabled);
            groupCredentials.Controls.Add(lblCredentialsCloudProvider);
            groupCredentials.Controls.Add(cmbCredentialsCloudProvider);
            groupCredentials.Controls.Add(lblCredentialsCloudPath);
            groupCredentials.Controls.Add(txtCredentialsCloudPath);
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
        }

        private void ApplyTheme()
        {
            BackColor = Branding.WindowBackground;
            ForeColor = Branding.HeadingText;

            ModernUi.ApplyGlassCard(groupQr, Localizer.Choose("QR Login and Hotkeys", "QR-вход и хоткеи"));
            ModernUi.ApplyGlassCard(groupCloud, Localizer.Choose("Cloud Sync", "Облачная синхронизация"));
            ModernUi.ApplyGlassCard(groupCredentials, Localizer.Choose("Credentials and Auto Login", "Логины и автовход"));
            labelLanguage.ForeColor = Branding.MutedText;
            label1.ForeColor = Branding.MutedText;

            foreach (Button button in Controls.OfType<Button>().Concat(groupQr.Controls.OfType<Button>()).Concat(groupCloud.Controls.OfType<Button>()).Concat(groupCredentials.Controls.OfType<Button>()))
            {
                button.FlatStyle = FlatStyle.Flat;
                button.FlatAppearance.BorderSize = 1;
                button.FlatAppearance.BorderColor = Branding.Outline;
                button.BackColor = Branding.AccentSoft;
                button.ForeColor = Branding.HeadingText;
                button.Font = new Font("Segoe UI Semibold", 9F, FontStyle.Regular, GraphicsUnit.Point);
                ModernUi.RoundButton(button, false);
            }

            btnSave.BackColor = Branding.Accent;
            btnSave.ForeColor = Color.White;
            btnSave.FlatAppearance.BorderColor = Branding.Accent;
            btnSave.Font = new Font("Segoe UI Semibold", 10.5F, FontStyle.Regular, GraphicsUnit.Point);
            ModernUi.RoundButton(btnSave, true);

            foreach (TextBox textBox in groupQr.Controls.OfType<TextBox>().Concat(groupCloud.Controls.OfType<TextBox>()))
            {
                ModernUi.WrapTextBox(textBox);
            }

            foreach (TextBox textBox in groupCredentials.Controls.OfType<TextBox>())
            {
                ModernUi.WrapTextBox(textBox);
            }

            foreach (ComboBox comboBox in Controls.OfType<ComboBox>()
                .Concat(groupQr.Controls.OfType<ComboBox>())
                .Concat(groupCloud.Controls.OfType<ComboBox>())
                .Concat(groupCredentials.Controls.OfType<ComboBox>()))
            {
                ModernUi.RoundComboBox(comboBox);
            }

            foreach (Label label in Controls.OfType<Label>()
                .Concat(groupQr.Controls.OfType<Label>())
                .Concat(groupCloud.Controls.OfType<Label>())
                .Concat(groupCredentials.Controls.OfType<Label>()))
            {
                if (label == lblCloudStatus || label == lblCloudLastSyncValue)
                {
                    continue;
                }

                label.ForeColor = Branding.MutedText;
            }

            foreach (CheckBox checkBox in Controls.OfType<CheckBox>()
                .Concat(groupQr.Controls.OfType<CheckBox>())
                .Concat(groupCloud.Controls.OfType<CheckBox>())
                .Concat(groupCredentials.Controls.OfType<CheckBox>()))
            {
                checkBox.ForeColor = Branding.HeadingText;
                checkBox.UseVisualStyleBackColor = false;
                checkBox.BackColor = checkBox.Parent?.BackColor ?? Branding.WindowBackground;
            }

            numPeriodicInterval.BackColor = Branding.AccentDark;
            numPeriodicInterval.ForeColor = Branding.HeadingText;
            numCursorScanSize.BackColor = Branding.AccentDark;
            numCursorScanSize.ForeColor = Branding.HeadingText;
            lblCloudStatus.ForeColor = Branding.MutedText;
        }

        private void ApplyCompactLayout()
        {
            if (compactLayoutApplied)
            {
                return;
            }

            compactLayoutApplied = true;
            ClientSize = new Size(760, 1028);

            labelLanguage.Location = new Point(12, 48);
            cmbLanguage.Location = new Point(70, 44);
            cmbLanguage.Size = new Size(148, 24);

            chkPeriodicChecking.Location = new Point(12, 82);
            chkPeriodicChecking.MaximumSize = new Size(338, 0);
            chkPeriodicChecking.AutoSize = true;

            numPeriodicInterval.Location = new Point(12, 138);
            numPeriodicInterval.Size = new Size(52, 22);
            label1.Location = new Point(72, 136);

            chkCheckAll.Location = new Point(12, 172);
            chkConfirmMarket.Location = new Point(12, 198);
            chkConfirmTrades.Location = new Point(12, 224);

            groupQr.Location = new Point(12, 260);
            groupQr.Size = new Size(348, 404);
            groupQr.Padding = new Padding(14, 28, 14, 14);
            LayoutQrGroup();

            groupCredentials.Location = new Point(12, 676);
            groupCredentials.Size = new Size(348, 290);
            groupCredentials.Padding = new Padding(14, 28, 14, 14);
            LayoutCredentialsGroup();

            groupCloud.Location = new Point(376, 48);
            groupCloud.Size = new Size(372, 918);
            groupCloud.Padding = new Padding(14, 28, 14, 14);
            LayoutCloudGroup();

            btnSave.Location = new Point(376, 978);
            btnSave.Size = new Size(372, 42);
        }

        private void LayoutQrGroup()
        {
            label3.Location = new Point(14, 20);
            txtHotkeyToggle.Location = new Point(14, 38);
            txtHotkeyToggle.Size = new Size(320, 24);

            label4.Location = new Point(14, 72);
            txtHotkeyScan.Location = new Point(14, 90);
            txtHotkeyScan.Size = new Size(320, 24);

            label5.Location = new Point(14, 124);
            txtHotkeyPrev.Location = new Point(14, 142);
            txtHotkeyPrev.Size = new Size(320, 24);

            label6.Location = new Point(14, 176);
            txtHotkeyNext.Location = new Point(14, 194);
            txtHotkeyNext.Size = new Size(320, 24);

            label7.Location = new Point(14, 230);
            cmbQrCaptureMode.Location = new Point(14, 248);
            cmbQrCaptureMode.Size = new Size(320, 24);

            lblCursorScanSize.Location = new Point(14, 286);
            numCursorScanSize.Location = new Point(160, 284);
            numCursorScanSize.Size = new Size(70, 22);
            btnResetHotkeys.Location = new Point(238, 281);
            btnResetHotkeys.Size = new Size(96, 28);

            chkEnableQrHotkeys.Location = new Point(14, 330);
            chkEnableQrHotkeys.MaximumSize = new Size(320, 0);
        }

        private void LayoutCloudGroup()
        {
            lblCloudProvider.Location = new Point(14, 20);
            lblCloudProvider.AutoSize = true;
            cmbCloudProvider.Location = new Point(14, 38);
            cmbCloudProvider.Size = new Size(344, 24);

            label8.Location = new Point(14, 74);
            txtWebDavUrl.Location = new Point(14, 92);
            txtWebDavUrl.Size = new Size(344, 24);

            label9.Location = new Point(14, 128);
            txtWebDavUsername.Location = new Point(14, 146);
            txtWebDavUsername.Size = new Size(344, 24);

            label10.Location = new Point(14, 182);
            txtWebDavPassword.Location = new Point(14, 200);
            txtWebDavPassword.Size = new Size(344, 24);

            lblCloudExtra.Location = new Point(14, 236);
            lblCloudExtra.AutoSize = true;
            txtCloudExtra.Location = new Point(14, 254);
            txtCloudExtra.Size = new Size(344, 24);

            label11.Location = new Point(14, 290);
            txtWebDavPath.Location = new Point(14, 308);
            txtWebDavPath.Size = new Size(344, 24);

            btnOneDriveSignIn.Location = new Point(14, 146);
            btnOneDriveSignIn.Size = new Size(344, 30);

            chkSyncStoredCredentials.Location = new Point(14, 346);
            chkSyncStoredCredentials.MaximumSize = new Size(344, 0);

            btnCloudTest.Location = new Point(14, 386);
            btnCloudTest.Size = new Size(106, 30);
            btnCloudPull.Location = new Point(132, 386);
            btnCloudPull.Size = new Size(106, 30);
            btnCloudPush.Location = new Point(250, 386);
            btnCloudPush.Size = new Size(108, 30);

            btnCloudOpenBackups.Location = new Point(14, 428);
            btnCloudOpenBackups.Size = new Size(344, 30);

            lblCloudStatus.Location = new Point(14, 470);
            lblCloudStatus.Size = new Size(344, 38);
            lblCloudLastSyncTitle.Location = new Point(14, 516);
            lblCloudLastSyncValue.Location = new Point(14, 538);
            lblCloudLastSyncValue.Size = new Size(344, 42);
        }

        private void LayoutCredentialsGroup()
        {
            chkAutoLoginExpired.Location = new Point(14, 20);
            chkAutoLoginExpired.MaximumSize = new Size(320, 0);

            chkAskBeforeAutoLogin.Location = new Point(14, 50);
            chkAskBeforeAutoLogin.MaximumSize = new Size(320, 0);

            lblCredentialsPath.Location = new Point(14, 86);
            lblCredentialsPath.Size = new Size(320, 18);
            txtCredentialsPath.Location = new Point(14, 106);
            txtCredentialsPath.Size = new Size(226, 24);
            btnBrowseCredentialsPath.Location = new Point(246, 104);
            btnBrowseCredentialsPath.Size = new Size(88, 28);

            chkRequireCredentialsEncryption.Location = new Point(14, 144);
            chkRequireCredentialsEncryption.MaximumSize = new Size(320, 0);

            chkAllowPlainCredentials.Location = new Point(14, 174);
            chkAllowPlainCredentials.MaximumSize = new Size(320, 0);

            chkCredentialsCloudEnabled.Location = new Point(14, 206);
            chkCredentialsCloudEnabled.MaximumSize = new Size(320, 0);

            lblCredentialsCloudProvider.Location = new Point(14, 234);
            lblCredentialsCloudProvider.Size = new Size(120, 18);
            cmbCredentialsCloudProvider.Location = new Point(140, 230);
            cmbCredentialsCloudProvider.Size = new Size(194, 24);

            lblCredentialsCloudPath.Location = new Point(14, 260);
            lblCredentialsCloudPath.Size = new Size(120, 18);
            txtCredentialsCloudPath.Location = new Point(140, 256);
            txtCredentialsCloudPath.Size = new Size(194, 24);
        }

        private void ApplyLocalization()
        {
            ModernUi.ApplyGlassCard(groupQr, Localizer.Choose("QR Login and Hotkeys", "QR-вход и хоткеи"));
            ModernUi.ApplyGlassCard(groupCloud, Localizer.Choose("Cloud Sync", "Облачная синхронизация"));
            ModernUi.ApplyGlassCard(groupCredentials, Localizer.Choose("Credentials and Auto Login", "Логины и автовход"));
            Text = Localizer.Choose("Settings", "Настройки");
            labelLanguage.Text = Localizer.Choose("Language:", "Язык:");
            chkPeriodicChecking.Text = Localizer.Choose(
                "Periodically check for new confirmations\r\nand show a popup when they arrive",
                "Периодически проверять новые подтверждения\r\nи показывать всплывающее окно");
            label1.Text = Localizer.Choose("Seconds between checking \r\nfor confirmations", "Секунд между проверками\r\nподтверждений");
            chkCheckAll.Text = Localizer.Choose("Check all accounts for confirmations", "Проверять подтверждения у всех аккаунтов");
            chkConfirmMarket.Text = Localizer.Choose("Auto-confirm market transactions", "Автоподтверждение продаж на маркете");
            chkConfirmTrades.Text = Localizer.Choose("Auto-confirm trades", "Автоподтверждение обменов");
            groupQr.Tag = Localizer.Choose("QR Login and Hotkeys", "QR-вход и хоткеи");
            chkEnableQrHotkeys.Text = Localizer.Choose("Start with QR hotkeys enabled", "Запускать с включенными QR-хоткеями");
            label3.Text = Localizer.Choose("Toggle QR hotkey mode:", "Переключить режим QR-хоткеев:");
            label4.Text = Localizer.Choose("Trigger QR scan:", "Запустить скан QR:");
            label5.Text = Localizer.Choose("Switch to previous account:", "Переключить на предыдущий аккаунт:");
            label6.Text = Localizer.Choose("Switch to next account:", "Переключить на следующий аккаунт:");
            label7.Text = Localizer.Choose("QR capture source:", "Источник захвата QR:");
            lblCursorScanSize.Text = Localizer.Choose("Cursor area size (pixels):", "Размер области у курсора (пикс.):");
            btnResetHotkeys.Text = Localizer.Choose("Reset defaults", "Сбросить");
            groupCloud.Tag = Localizer.Choose("Cloud Sync", "Облачная синхронизация");
            lblCloudProvider.Text = Localizer.Choose("Provider:", "Провайдер:");
            label11.Text = Localizer.Choose("Remote folder path:", "Путь к папке в облаке:");
            chkSyncStoredCredentials.Text = Localizer.Choose("Sync saved SDA++ auto-login credentials too", "Синхронизировать сохраненные логины SDA++");
            btnCloudTest.Text = Localizer.Choose("Test connection", "Проверить соединение");
            btnCloudPull.Text = Localizer.Choose("Pull from cloud", "Загрузить из облака");
            btnCloudPush.Text = Localizer.Choose("Push to cloud", "Отправить в облако");
            lblCloudStatus.Text = Localizer.Choose("Use Test connection before Pull or Push.", "Сначала проверьте соединение, затем выполните загрузку или отправку.");
            btnSave.Text = Localizer.Choose("Save", "Сохранить");
            btnCloudOpenBackups.Text = Localizer.Choose("Open backup folder", "Открыть папку резервных копий");
            lblCloudLastSyncTitle.Text = Localizer.Choose("Last sync:", "Последняя синхронизация:");
            btnOneDriveSignIn.Text = Localizer.Choose("Sign in to Microsoft account", "Войти в аккаунт Microsoft");
            chkAutoLoginExpired.Text = Localizer.Choose("Enable auto login for expired sessions", "Включить автовход для истекших сессий");
            chkAskBeforeAutoLogin.Text = Localizer.Choose("Ask before auto login", "Спрашивать перед автовходом");
            lblCredentialsPath.Text = Localizer.Choose("Credentials storage location:", "Путь к файлу логинов:");
            btnBrowseCredentialsPath.Text = Localizer.Choose("Browse", "Обзор");
            chkRequireCredentialsEncryption.Text = Localizer.Choose("Require encryption for credentials", "Требовать шифрование логинов");
            chkAllowPlainCredentials.Text = Localizer.Choose("Allow unencrypted local credentials file after warning", "Разрешать локальный файл логинов без шифрования после предупреждения");
            chkCredentialsCloudEnabled.Text = Localizer.Choose("Store credentials in cloud sync", "Хранить логины в облачной синхронизации");
            lblCredentialsCloudProvider.Text = Localizer.Choose("Cloud provider:", "Провайдер:");
            lblCredentialsCloudPath.Text = Localizer.Choose("Cloud folder:", "Папка в облаке:");
            ReloadCloudProviders();
            ReloadCredentialsCloudProviders();
            UpdateCloudProviderUi();
            ReloadCaptureModes();
            RefreshCloudLastSyncInfo();
        }

        private void ReloadCaptureModes()
        {
            int selected = cmbQrCaptureMode.SelectedIndex < 0 ? 0 : cmbQrCaptureMode.SelectedIndex;
            cmbQrCaptureMode.Items.Clear();
            cmbQrCaptureMode.Items.AddRange(new object[]
            {
                Localizer.Choose("Full desktop", "Весь рабочий стол"),
                Localizer.Choose("Monitor under cursor", "Монитор под курсором"),
                Localizer.Choose("Area around cursor", "Область вокруг курсора")
            });
            cmbQrCaptureMode.SelectedIndex = selected;
        }

        private void ReloadCloudProviders()
        {
            int selected = cmbCloudProvider.SelectedIndex;
            cmbCloudProvider.Items.Clear();
            cmbCloudProvider.Items.AddRange(new object[]
            {
                "WebDAV",
                Localizer.Choose("S3-compatible (Backblaze B2 / Cloudflare R2)", "S3-compatible (Backblaze B2 / Cloudflare R2)"),
                "Dropbox",
                "OneDrive Personal",
                "Google Drive"
            });
            cmbCloudProvider.SelectedIndex = selected >= 0 ? selected : (manifest == null ? 0 : (int)manifest.CloudProvider);
        }

        private void ReloadCredentialsCloudProviders()
        {
            int selected = cmbCredentialsCloudProvider.SelectedIndex;
            cmbCredentialsCloudProvider.Items.Clear();
            cmbCredentialsCloudProvider.Items.AddRange(new object[]
            {
                "WebDAV",
                Localizer.Choose("S3-compatible (Backblaze B2 / Cloudflare R2)", "S3-compatible (Backblaze B2 / Cloudflare R2)"),
                "Dropbox",
                "OneDrive Personal",
                "Google Drive"
            });
            cmbCredentialsCloudProvider.SelectedIndex = selected >= 0 ? selected : (int)manifest.CredentialsCloudProvider;
        }

        private void InitializeCloudDrafts()
        {
            string webDavPassword = cloudSecretStore.Load("webdav-password");
            if (string.IsNullOrEmpty(webDavPassword))
            {
                webDavPassword = legacyWebDavSecretStore.LoadPassword();
            }

            cloudDrafts[CloudProvider.WebDav] = new CloudProviderDraft
            {
                Field1 = manifest.WebDavUrl,
                Field2 = manifest.WebDavUsername,
                Field3 = webDavPassword,
                RemotePath = manifest.WebDavRemotePath
            };
            cloudDrafts[CloudProvider.S3Compatible] = new CloudProviderDraft
            {
                Field1 = manifest.S3Endpoint,
                Field2 = manifest.S3Bucket,
                Field3 = cloudSecretStore.Load("s3-secret-key"),
                Extra = manifest.S3AccessKey,
                RemotePath = manifest.S3RemotePath
            };
            cloudDrafts[CloudProvider.Dropbox] = new CloudProviderDraft
            {
                Field1 = cloudSecretStore.Load("dropbox-access-token"),
                RemotePath = manifest.DropboxRemotePath
            };
            cloudDrafts[CloudProvider.OneDrivePersonal] = new CloudProviderDraft
            {
                Field1 = manifest.OneDriveClientId,
                RemotePath = manifest.OneDriveRemotePath
            };
            cloudDrafts[CloudProvider.GoogleDrive] = new CloudProviderDraft
            {
                Field1 = manifest.GoogleDriveClientId,
                RemotePath = manifest.GoogleDriveRemotePath
            };
        }

        private void cmbCloudProvider_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (cmbCloudProvider.SelectedIndex < 0 || cloudDrafts.Count == 0)
            {
                return;
            }

            SaveActiveCloudDraft();
            activeCloudProvider = (CloudProvider)cmbCloudProvider.SelectedIndex;
            LoadActiveCloudDraft();
            UpdateCloudProviderUi();
        }

        private void SaveActiveCloudDraft()
        {
            if (activeCloudProvider == null || !cloudDrafts.TryGetValue(activeCloudProvider.Value, out CloudProviderDraft draft))
            {
                return;
            }

            draft.Field1 = txtWebDavUrl.Text.Trim();
            draft.Field2 = txtWebDavUsername.Text.Trim();
            draft.Field3 = txtWebDavPassword.Text;
            draft.Extra = txtCloudExtra.Text.Trim();
            draft.RemotePath = txtWebDavPath.Text.Trim();
        }

        private void LoadActiveCloudDraft()
        {
            CloudProviderDraft draft = cloudDrafts[activeCloudProvider ?? CloudProvider.WebDav];
            txtWebDavUrl.Text = draft.Field1;
            txtWebDavUsername.Text = draft.Field2;
            txtWebDavPassword.Text = draft.Field3;
            txtCloudExtra.Text = draft.Extra;
            txtWebDavPath.Text = draft.RemotePath;
        }

        private void UpdateCloudProviderUi()
        {
            if (cmbCloudProvider.SelectedIndex < 0)
            {
                return;
            }

            CloudProvider provider = (CloudProvider)cmbCloudProvider.SelectedIndex;
            bool webDav = provider == CloudProvider.WebDav;
            bool s3 = provider == CloudProvider.S3Compatible;
            bool dropbox = provider == CloudProvider.Dropbox;
            bool oneDrive = provider == CloudProvider.OneDrivePersonal;
            bool googleDrive = provider == CloudProvider.GoogleDrive;

            label8.Text = provider switch
            {
                CloudProvider.WebDav => Localizer.Choose("WebDAV URL:", "URL WebDAV:"),
                CloudProvider.S3Compatible => Localizer.Choose("S3 endpoint:", "S3 endpoint:"),
                CloudProvider.Dropbox => Localizer.Choose("Dropbox access token:", "Access token Dropbox:"),
                CloudProvider.OneDrivePersonal => Localizer.Choose("Microsoft Entra Client ID:", "Client ID Microsoft Entra:"),
                _ => Localizer.Choose("Google OAuth Client ID:", "Google OAuth Client ID:")
            };
            label9.Text = s3
                ? Localizer.Choose("Bucket:", "Bucket:")
                : Localizer.Choose("WebDAV username:", "Логин WebDAV:");
            label10.Text = s3
                ? Localizer.Choose("Secret key:", "Secret key:")
                : Localizer.Choose("WebDAV password:", "Пароль WebDAV:");
            lblCloudExtra.Text = Localizer.Choose("Access key ID:", "Access key ID:");

            txtWebDavUrl.PasswordChar = dropbox ? '*' : '\0';
            txtWebDavPassword.PasswordChar = (webDav || s3) ? '*' : '\0';
            SetCloudFieldVisible(label9, txtWebDavUsername, webDav || s3);
            SetCloudFieldVisible(label10, txtWebDavPassword, webDav || s3);
            SetCloudFieldVisible(lblCloudExtra, txtCloudExtra, s3);
            btnOneDriveSignIn.Visible = oneDrive || googleDrive;

            if (dropbox || oneDrive || googleDrive)
            {
                label11.Location = new Point(14, 182);
                SetCloudControlLocation(txtWebDavPath, new Point(14, 200));
                chkSyncStoredCredentials.Location = new Point(14, 238);
            }
            else
            {
                label11.Location = new Point(14, 290);
                SetCloudControlLocation(txtWebDavPath, new Point(14, 308));
                chkSyncStoredCredentials.Location = new Point(14, 346);
            }

            btnOneDriveSignIn.Location = new Point(14, 146);
            btnOneDriveSignIn.Text = oneDrive
                ? Localizer.Choose("Sign in to Microsoft account", "Войти в аккаунт Microsoft")
                : Localizer.Choose("Sign in to Google account", "Войти в аккаунт Google");
            lblCloudStatus.Text = Localizer.Choose(
                "Use Test connection before Pull or Push.",
                "Сначала проверьте соединение, затем выполните загрузку или отправку.");
        }

        private static void SetCloudFieldVisible(Label label, TextBox textBox, bool visible)
        {
            label.Visible = visible;
            Control shell = textBox.Parent is Panel panel && Equals(panel.Tag, "glass-shell") ? panel : textBox;
            shell.Visible = visible;
        }

        private static void SetCloudControlLocation(TextBox textBox, Point location)
        {
            Control shell = textBox.Parent is Panel panel && Equals(panel.Tag, "glass-shell") ? panel : textBox;
            shell.Location = location;
        }

        private void SetControlsEnabledState(bool enabled)
        {
            chkCheckAll.ForeColor = enabled ? Branding.HeadingText : Branding.MutedText;
            chkConfirmMarket.ForeColor = enabled ? Branding.HeadingText : Branding.MutedText;
            chkConfirmTrades.ForeColor = enabled ? Branding.HeadingText : Branding.MutedText;
            label1.ForeColor = enabled ? Branding.MutedText : Branding.Outline;
            numPeriodicInterval.ReadOnly = !enabled;
            numPeriodicInterval.ForeColor = enabled ? Branding.HeadingText : Branding.MutedText;
        }

        private void UpdateQrControls()
        {
            bool areaMode = cmbQrCaptureMode.SelectedIndex == (int)QrCaptureMode.AreaAroundCursor;
            numCursorScanSize.ReadOnly = !areaMode;
            numCursorScanSize.ForeColor = areaMode ? Branding.HeadingText : Branding.MutedText;
            lblCursorScanSize.ForeColor = areaMode ? Branding.MutedText : Branding.Outline;
        }

        private void ShowWarning(CheckBox affectedBox)
        {
            if (!fullyLoaded) return;

            var result = MessageBox.Show(
                Localizer.Choose(
                    "Warning: enabling this will severely reduce the security of your items! Use of this option is at your own risk. Would you like to continue?",
                    "Внимание: включение этой функции может заметно снизить безопасность ваших предметов. Используйте ее на свой риск. Продолжить?"),
                Localizer.Choose("Warning", "Предупреждение"),
                MessageBoxButtons.YesNo);
            if (result == DialogResult.No)
            {
                affectedBox.Checked = false;
            }
        }

        private void btnSave_Click(object sender, EventArgs e)
        {
            Dictionary<string, HotkeyBinding> bindings = new Dictionary<string, HotkeyBinding>
            {
                { "QR mode toggle", GetBindingFromBox(txtHotkeyToggle, Keys.Q) },
                { "QR scan", GetBindingFromBox(txtHotkeyScan, Keys.S) },
                { "Previous account", GetBindingFromBox(txtHotkeyPrev, Keys.Left) },
                { "Next account", GetBindingFromBox(txtHotkeyNext, Keys.Right) }
            };

            if (bindings.Values.GroupBy(HotkeyBindingHelper.ToDisplayText).Any(group => group.Count() > 1))
            {
                MessageBox.Show(Localizer.Choose("Each hotkey must be unique.", "Каждый хоткей должен быть уникальным."), Localizer.Choose("Settings", "Настройки"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            manifest.PeriodicChecking = chkPeriodicChecking.Checked;
            manifest.PeriodicCheckingInterval = (int)numPeriodicInterval.Value;
            manifest.CheckAllAccounts = chkCheckAll.Checked;
            manifest.AutoConfirmMarketTransactions = chkConfirmMarket.Checked;
            manifest.AutoConfirmTrades = chkConfirmTrades.Checked;
            manifest.QrHotkeysEnabled = chkEnableQrHotkeys.Checked;
            manifest.QrHotkeyToggle = bindings["QR mode toggle"];
            manifest.QrHotkeyScan = bindings["QR scan"];
            manifest.AccountHotkeyPrevious = bindings["Previous account"];
            manifest.AccountHotkeyNext = bindings["Next account"];
            manifest.QrCaptureMode = (QrCaptureMode)cmbQrCaptureMode.SelectedIndex;
            manifest.QrCursorScanSize = (int)numCursorScanSize.Value;
            manifest.AutoLoginForExpiredSessions = chkAutoLoginExpired.Checked;
            manifest.AskBeforeAutoLogin = chkAskBeforeAutoLogin.Checked;
            manifest.CredentialsStoragePath = txtCredentialsPath.Text.Trim();
            manifest.CredentialsRequireEncryption = chkRequireCredentialsEncryption.Checked;
            manifest.AllowUnencryptedLocalCredentials = chkAllowPlainCredentials.Checked;
            manifest.CredentialsCloudEnabled = chkCredentialsCloudEnabled.Checked;
            manifest.CredentialsCloudProvider = (CloudProvider)cmbCredentialsCloudProvider.SelectedIndex;
            manifest.CredentialsCloudRemotePath = txtCredentialsCloudPath.Text.Trim();
            SaveActiveCloudDraft();
            SaveCloudSettings();
            manifest.WebDavSyncStoredCredentials = chkSyncStoredCredentials.Checked;
            manifest.UiLanguage = (AppLanguage)cmbLanguage.SelectedIndex;
            Localizer.SetLanguage(manifest.UiLanguage);
            manifest.Save();
            Close();
        }

        private void chkPeriodicChecking_CheckedChanged(object sender, EventArgs e)
        {
            SetControlsEnabledState(chkPeriodicChecking.Checked);
        }

        private void chkConfirmMarket_CheckedChanged(object sender, EventArgs e)
        {
            if (chkConfirmMarket.Checked)
                ShowWarning(chkConfirmMarket);
        }

        private void chkConfirmTrades_CheckedChanged(object sender, EventArgs e)
        {
            if (chkConfirmTrades.Checked)
                ShowWarning(chkConfirmTrades);
        }

        private void cmbQrCaptureMode_SelectedIndexChanged(object sender, EventArgs e)
        {
            UpdateQrControls();
        }

        private void txtHotkey_KeyDown(object sender, KeyEventArgs e)
        {
            TextBox hotkeyBox = sender as TextBox;
            if (hotkeyBox == null)
            {
                return;
            }

            if (e.KeyCode == Keys.Delete || e.KeyCode == Keys.Back)
            {
                hotkeyBox.Tag = null;
                hotkeyBox.Text = Localizer.Choose("Not set", "Не задано");
                e.SuppressKeyPress = true;
                return;
            }

            HotkeyBinding binding = HotkeyBindingHelper.FromKeyEvent(e);
            if (!HotkeyBindingHelper.IsValid(binding))
            {
                return;
            }

            hotkeyBox.Tag = binding;
            hotkeyBox.Text = HotkeyBindingHelper.ToDisplayText(binding);
            e.SuppressKeyPress = true;
        }

        private void btnResetHotkeys_Click(object sender, EventArgs e)
        {
            BindHotkeyBox(txtHotkeyToggle, HotkeyBindingHelper.CreateDefault(Keys.Q));
            BindHotkeyBox(txtHotkeyScan, HotkeyBindingHelper.CreateDefault(Keys.S));
            BindHotkeyBox(txtHotkeyPrev, HotkeyBindingHelper.CreateDefault(Keys.Left));
            BindHotkeyBox(txtHotkeyNext, HotkeyBindingHelper.CreateDefault(Keys.Right));
        }

        private void BindHotkeyBox(TextBox hotkeyBox, HotkeyBinding binding)
        {
            HotkeyBinding clone = binding?.Clone();
            hotkeyBox.Tag = clone;
            hotkeyBox.Text = HotkeyBindingHelper.ToDisplayText(clone);
        }

        private static HotkeyBinding GetBindingFromBox(TextBox hotkeyBox, Keys fallback)
        {
            return HotkeyBindingHelper.Normalize((hotkeyBox.Tag as HotkeyBinding)?.Clone(), fallback);
        }

        private void SaveCloudSettings()
        {
            manifest.CloudProvider = activeCloudProvider ?? CloudProvider.WebDav;

            CloudProviderDraft webDav = cloudDrafts[CloudProvider.WebDav];
            manifest.WebDavUrl = webDav.Field1;
            manifest.WebDavUsername = webDav.Field2;
            manifest.WebDavRemotePath = webDav.RemotePath;
            cloudSecretStore.Save("webdav-password", webDav.Field3);

            CloudProviderDraft s3 = cloudDrafts[CloudProvider.S3Compatible];
            manifest.S3Endpoint = s3.Field1;
            manifest.S3Bucket = s3.Field2;
            manifest.S3AccessKey = s3.Extra;
            manifest.S3RemotePath = s3.RemotePath;
            cloudSecretStore.Save("s3-secret-key", s3.Field3);

            CloudProviderDraft dropbox = cloudDrafts[CloudProvider.Dropbox];
            manifest.DropboxRemotePath = dropbox.RemotePath;
            cloudSecretStore.Save("dropbox-access-token", dropbox.Field1);

            CloudProviderDraft oneDrive = cloudDrafts[CloudProvider.OneDrivePersonal];
            manifest.OneDriveClientId = oneDrive.Field1;
            manifest.OneDriveRemotePath = oneDrive.RemotePath;

            CloudProviderDraft googleDrive = cloudDrafts[CloudProvider.GoogleDrive];
            manifest.GoogleDriveClientId = googleDrive.Field1;
            manifest.GoogleDriveRemotePath = googleDrive.RemotePath;
        }

        private void btnBrowseCredentialsPath_Click(object sender, EventArgs e)
        {
            using SaveFileDialog dialog = new SaveFileDialog
            {
                Filter = Localizer.Choose("Credentials vault|*.json|All files|*.*", "Файл логинов|*.json|Все файлы|*.*"),
                FileName = Path.GetFileName(txtCredentialsPath.Text),
                InitialDirectory = Path.GetDirectoryName(Path.IsPathRooted(txtCredentialsPath.Text)
                    ? txtCredentialsPath.Text
                    : Path.Combine(Manifest.GetExecutableDir(), txtCredentialsPath.Text))
            };
            if (dialog.ShowDialog(this) == DialogResult.OK)
            {
                txtCredentialsPath.Text = dialog.FileName;
            }
        }

        private ICloudStorageProvider CreateCloudProvider()
        {
            SaveActiveCloudDraft();
            CloudProvider provider = activeCloudProvider ?? CloudProvider.WebDav;
            CloudProviderDraft draft = cloudDrafts[provider];
            return provider switch
            {
                CloudProvider.WebDav => new WebDavStorageProvider(
                    draft.Field1,
                    draft.Field2,
                    draft.Field3,
                    draft.RemotePath),
                CloudProvider.S3Compatible => new S3StorageProvider(
                    draft.Field1,
                    draft.Field2,
                    draft.Extra,
                    draft.Field3,
                    draft.RemotePath),
                CloudProvider.Dropbox => new DropboxStorageProvider(
                    draft.Field1,
                    draft.RemotePath),
                CloudProvider.OneDrivePersonal => new OneDriveStorageProvider(
                    draft.Field1,
                    draft.RemotePath,
                    cloudSecretStore),
                CloudProvider.GoogleDrive => new GoogleDriveStorageProvider(
                    draft.Field1,
                    draft.RemotePath,
                    cloudSecretStore),
                _ => throw new InvalidOperationException("Unsupported cloud provider.")
            };
        }

        private ICloudStorageProvider CreateCredentialsCloudProviderOrNull()
        {
            if (!chkSyncStoredCredentials.Checked || !chkCredentialsCloudEnabled.Checked)
            {
                return null;
            }

            CloudProvider provider = (CloudProvider)cmbCredentialsCloudProvider.SelectedIndex;
            CloudProviderDraft draft = cloudDrafts[provider];
            string remotePath = txtCredentialsCloudPath.Text.Trim();
            return provider switch
            {
                CloudProvider.WebDav => new WebDavStorageProvider(
                    draft.Field1,
                    draft.Field2,
                    draft.Field3,
                    remotePath),
                CloudProvider.S3Compatible => new S3StorageProvider(
                    draft.Field1,
                    draft.Field2,
                    draft.Extra,
                    draft.Field3,
                    remotePath),
                CloudProvider.Dropbox => new DropboxStorageProvider(
                    draft.Field1,
                    remotePath),
                CloudProvider.OneDrivePersonal => new OneDriveStorageProvider(
                    draft.Field1,
                    remotePath,
                    cloudSecretStore),
                CloudProvider.GoogleDrive => new GoogleDriveStorageProvider(
                    draft.Field1,
                    remotePath,
                    cloudSecretStore),
                _ => null
            };
        }

        private async void btnCloudTest_Click(object sender, EventArgs e)
        {
            await RunCloudActionAsync(Localizer.Choose("Testing connection...", "Проверка соединения..."), async () =>
            {
                string status = await UseCloudProviderAsync(provider => cloudSyncService.TestConnectionAsync(provider));
                lblCloudStatus.Text = status;
            });
        }

        private async void btnCloudPull_Click(object sender, EventArgs e)
        {
            DialogResult result = MessageBox.Show(
                Localizer.Choose(
                    "Pull will back up your current local vault and then replace local manifest/maFiles with the cloud copy.\n\nContinue?",
                    "Pull создаст резервную копию локальных данных, а затем заменит локальные manifest/maFiles копией из облака.\n\nПродолжить?"),
                Localizer.Choose("Cloud Pull", "Загрузка из облака"),
                MessageBoxButtons.OKCancel,
                MessageBoxIcon.Warning);

            if (result != DialogResult.OK)
            {
                return;
            }

            await RunCloudActionAsync(Localizer.Choose("Pulling from cloud...", "Загрузка из облака..."), async () =>
            {
                await UseCloudProvidersAsync((provider, credentialsProvider) => cloudSyncService.PullAsync(provider, chkSyncStoredCredentials.Checked, credentialsProvider));
                manifest = Manifest.GetManifest(true);
                UpdateCloudLastSync("pull", true);
                lblCloudStatus.Text = Localizer.Choose("Pull completed. Close Settings to reload accounts.", "Загрузка завершена. Закройте настройки, чтобы обновить аккаунты.");
            }, "pull");
        }

        private async void btnCloudPush_Click(object sender, EventArgs e)
        {
            if (chkSyncStoredCredentials.Checked && chkCredentialsCloudEnabled.Checked && !chkRequireCredentialsEncryption.Checked)
            {
                DialogResult plainWarning = MessageBox.Show(
                    Localizer.Choose(
                        "Credentials cloud upload is configured while the local credentials file is not encrypted.\n\nThis is dangerous because login and password data can be exposed if the cloud file is accessed by someone else.\n\nUpload anyway?",
                        "Включена облачная синхронизация логинов, но локальный файл логинов не зашифрован.\n\nЭто опасно: логины и пароли могут утечь, если кто-то получит доступ к файлу в облаке.\n\nВсе равно отправить?"),
                    Localizer.Choose("Credentials cloud warning", "Предупреждение по облаку логинов"),
                    MessageBoxButtons.OKCancel,
                    MessageBoxIcon.Warning);
                if (plainWarning != DialogResult.OK)
                {
                    return;
                }
            }

            DialogResult result = MessageBox.Show(
                Localizer.Choose(
                    "Push will upload your current local vault to the selected cloud provider.\n\nContinue?",
                    "Отправка выгрузит текущее локальное хранилище выбранному облачному провайдеру.\n\nПродолжить?"),
                Localizer.Choose("Cloud Push", "Отправка в облако"),
                MessageBoxButtons.OKCancel,
                MessageBoxIcon.Information);

            if (result != DialogResult.OK)
            {
                return;
            }

            await RunCloudActionAsync(Localizer.Choose("Pushing to cloud...", "Отправка в облако..."), async () =>
            {
                await UseCloudProvidersAsync((provider, credentialsProvider) => cloudSyncService.PushAsync(provider, chkSyncStoredCredentials.Checked, credentialsProvider));
                UpdateCloudLastSync("push", true);
                lblCloudStatus.Text = Localizer.Choose("Push completed.", "Отправка завершена.");
            }, "push");
        }

        private async void btnOneDriveSignIn_Click(object sender, EventArgs e)
        {
            CloudProvider providerType = activeCloudProvider ?? CloudProvider.WebDav;
            await RunCloudActionAsync(
                providerType == CloudProvider.GoogleDrive
                    ? Localizer.Choose("Waiting for Google sign in...", "Ожидание входа Google...")
                    : Localizer.Choose("Waiting for Microsoft sign in...", "Ожидание входа Microsoft..."),
                async () =>
                {
                    SaveActiveCloudDraft();
                    switch (providerType)
                    {
                        case CloudProvider.OneDrivePersonal:
                            {
                                CloudProviderDraft draft = cloudDrafts[CloudProvider.OneDrivePersonal];
                                var provider = new OneDriveStorageProvider(draft.Field1, draft.RemotePath, cloudSecretStore);
                                await provider.SignInAsync();
                                lblCloudStatus.Text = Localizer.Choose(
                                    "Microsoft account connected.",
                                    "Аккаунт Microsoft подключен.");
                                break;
                            }
                        case CloudProvider.GoogleDrive:
                            {
                                CloudProviderDraft draft = cloudDrafts[CloudProvider.GoogleDrive];
                                var provider = new GoogleDriveStorageProvider(draft.Field1, draft.RemotePath, cloudSecretStore);
                                await provider.SignInAsync();
                                lblCloudStatus.Text = Localizer.Choose(
                                    "Google account connected.",
                                    "Аккаунт Google подключен.");
                                break;
                            }
                        default:
                            throw new InvalidOperationException(Localizer.Choose(
                                "This sign-in action is only available for OneDrive Personal and Google Drive.",
                                "Это действие входа доступно только для OneDrive Personal и Google Drive."));
                    }
                });
        }

        private async Task UseCloudProviderAsync(Func<ICloudStorageProvider, Task> action)
        {
            ICloudStorageProvider provider = CreateCloudProvider();
            try
            {
                await action(provider);
            }
            finally
            {
                (provider as IDisposable)?.Dispose();
            }
        }

        private async Task<T> UseCloudProviderAsync<T>(Func<ICloudStorageProvider, Task<T>> action)
        {
            ICloudStorageProvider provider = CreateCloudProvider();
            try
            {
                return await action(provider);
            }
            finally
            {
                (provider as IDisposable)?.Dispose();
            }
        }

        private async Task UseCloudProvidersAsync(Func<ICloudStorageProvider, ICloudStorageProvider, Task> action)
        {
            ICloudStorageProvider provider = CreateCloudProvider();
            ICloudStorageProvider credentialsProvider = null;
            try
            {
                credentialsProvider = CreateCredentialsCloudProviderOrNull();
                await action(provider, credentialsProvider);
            }
            finally
            {
                (credentialsProvider as IDisposable)?.Dispose();
                (provider as IDisposable)?.Dispose();
            }
        }

        private void btnCloudOpenBackups_Click(object sender, EventArgs e)
        {
            string backupDir = GetCloudBackupDirectory();
            Directory.CreateDirectory(backupDir);

            Process.Start(new ProcessStartInfo
            {
                FileName = backupDir,
                UseShellExecute = true
            });
        }

        private async Task RunCloudActionAsync(string busyText, Func<Task> action, string syncAction = null)
        {
            SetCloudControlsEnabled(false);
            lblCloudStatus.Text = busyText;

            try
            {
                await action();
            }
            catch (Exception ex)
            {
                lblCloudStatus.Text = ex.Message;
                if (!string.IsNullOrWhiteSpace(syncAction))
                {
                    UpdateCloudLastSync(syncAction, false);
                }
                MessageBox.Show(ex.Message, Localizer.Choose("Cloud Sync", "Облачная синхронизация"), MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                SetCloudControlsEnabled(true);
            }
        }

        private void SetCloudControlsEnabled(bool enabled)
        {
            cmbCloudProvider.Enabled = enabled;
            btnCloudTest.Enabled = enabled;
            btnCloudPull.Enabled = enabled;
            btnCloudPush.Enabled = enabled;
            btnCloudOpenBackups.Enabled = enabled;
            btnOneDriveSignIn.Enabled = enabled;
            txtWebDavUrl.Enabled = enabled;
            txtWebDavUsername.Enabled = enabled;
            txtWebDavPassword.Enabled = enabled;
            txtCloudExtra.Enabled = enabled;
            txtWebDavPath.Enabled = enabled;
            chkSyncStoredCredentials.Enabled = enabled;
            chkCredentialsCloudEnabled.Enabled = enabled;
            cmbCredentialsCloudProvider.Enabled = enabled;
            txtCredentialsCloudPath.Enabled = enabled;
        }

        private void UpdateCloudLastSync(string action, bool success)
        {
            manifest = Manifest.GetManifest(true);
            manifest.WebDavLastSyncAction = action ?? string.Empty;
            manifest.WebDavLastSyncSuccess = success;
            manifest.WebDavLastSyncUtc = DateTime.UtcNow;
            manifest.Save();
            RefreshCloudLastSyncInfo();
        }

        private void RefreshCloudLastSyncInfo()
        {
            lblCloudLastSyncValue.Text = FormatCloudLastSyncText();
            if (manifest.WebDavLastSyncUtc == null || string.IsNullOrWhiteSpace(manifest.WebDavLastSyncAction))
            {
                lblCloudLastSyncValue.ForeColor = Branding.MutedText;
            }
            else if (manifest.WebDavLastSyncSuccess == true)
            {
                lblCloudLastSyncValue.ForeColor = Branding.Accent;
            }
            else
            {
                lblCloudLastSyncValue.ForeColor = Branding.Danger;
            }
        }

        private string FormatCloudLastSyncText()
        {
            if (manifest.WebDavLastSyncUtc == null || string.IsNullOrWhiteSpace(manifest.WebDavLastSyncAction))
            {
                return Localizer.Choose("Not synced yet.", "Синхронизации еще не было.");
            }

            string actionText;
            switch (manifest.WebDavLastSyncAction)
            {
                case "pull":
                    actionText = Localizer.Choose("Pull", "Загрузка");
                    break;
                case "push":
                    actionText = Localizer.Choose("Push", "Отправка");
                    break;
                default:
                    actionText = Localizer.Choose("Sync", "Синхронизация");
                    break;
            }

            string statusText = manifest.WebDavLastSyncSuccess == true
                ? Localizer.Choose("completed", "завершена")
                : Localizer.Choose("failed", "с ошибкой");

            string timestamp = manifest.WebDavLastSyncUtc.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
            return Localizer.Choose(
                $"{actionText} {statusText} at {timestamp}.",
                $"{actionText} {statusText} в {timestamp}.");
        }

        private static string GetCloudBackupDirectory()
        {
            return Path.Combine(Manifest.GetExecutableDir(), "maFiles", "backups", "cloudsync");
        }
    }
}
