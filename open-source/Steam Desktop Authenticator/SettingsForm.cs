using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
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
        private readonly Label lblHotkeyAutoLoginAll = new Label();
        private readonly TextBox txtHotkeyAutoLoginAll = new TextBox();
        private readonly Label lblHotkeyConfirmations = new Label();
        private readonly TextBox txtHotkeyConfirmations = new TextBox();
        private readonly Button btnAppearance = new Button();
        private readonly Panel panelSettingsNav = new Panel();
        private readonly Panel panelGeneralPage = new Panel();
        private readonly Panel panelQrPage = new Panel();
        private readonly Panel panelCloudPage = new Panel();
        private readonly Panel panelCredentialsPage = new Panel();
        private readonly Panel panelAppearancePage = new Panel();
        private readonly Panel panelMonitoringPage = new Panel();
        private readonly Panel panelMobilePage = new Panel();
        private readonly Button btnPageGeneral = new Button();
        private readonly Button btnPageQr = new Button();
        private readonly Button btnPageCloud = new Button();
        private readonly Button btnPageCredentials = new Button();
        private readonly Button btnPageAppearance = new Button();
        private readonly Button btnPageMonitoring = new Button();
        private readonly Button btnPageMobile = new Button();
        private readonly Label lblSettingsNavTitle = new Label();
        private readonly Label lblGeneralTitle = new Label();
        private readonly GroupBox groupGeneralPreferences = new GroupBox();
        private readonly GroupBox groupConfirmationAutomation = new GroupBox();
        private readonly Label lblAppearanceTitle = new Label();
        private readonly Label lblAppearanceIntro = new Label();
        private readonly Button btnThemeGraphite = new Button();
        private readonly Button btnThemeObsidian = new Button();
        private readonly Button btnThemeSlate = new Button();
        private readonly Label lblThemeGraphite = new Label();
        private readonly Label lblThemeObsidian = new Label();
        private readonly Label lblThemeSlate = new Label();
        private readonly GroupBox groupMonitoring = new GroupBox();
        private readonly GroupBox groupMobile = new GroupBox();
        private readonly Label lblMonitoringDescription = new Label();
        private readonly Label lblMobileDescription = new Label();
        private readonly Button btnOpenMonitoring = new Button();
        private readonly Button btnOpenMobilePairing = new Button();
        private readonly Button btnOpenCredentialsManager = new Button();
        private readonly Action<IWin32Window> openCredentialsManager;
        private readonly Action<IWin32Window> openMobilePairing;
        private readonly Action<IWin32Window> openAccountMonitoring;
        private Manifest manifest;
        private bool fullyLoaded = false;
        private bool compactLayoutApplied = false;
        private CloudProvider? activeCloudProvider;
        private readonly Timer pageTransitionTimer = new Timer { Interval = 15 };
        private readonly Stopwatch pageTransitionWatch = new Stopwatch();
        private Panel activeSettingsPage;
        private Panel transitionFromPage;
        private Panel transitionToPage;
        private const int SettingsPageLeft = 228;
        private const int SettingsPageTop = 52;
        private const int PageTransitionDurationMs = 180;

        private sealed class CloudProviderDraft
        {
            public string Field1 { get; set; } = string.Empty;
            public string Field2 { get; set; } = string.Empty;
            public string Field3 { get; set; } = string.Empty;
            public string Extra { get; set; } = string.Empty;
            public string RemotePath { get; set; } = "SDAppVault";
        }

        public SettingsForm(
            Action<IWin32Window> openCredentialsManager = null,
            Action<IWin32Window> openMobilePairing = null,
            Action<IWin32Window> openAccountMonitoring = null)
        {
            this.openCredentialsManager = openCredentialsManager;
            this.openMobilePairing = openMobilePairing;
            this.openAccountMonitoring = openAccountMonitoring;
            InitializeComponent();
            Icon = Branding.LoadAppIcon();
            CreateActionHotkeyControls();
            CreateCloudProviderControls();
            CreateCredentialsControls();
            CreateAppearanceControl();
            CreateAppearancePage();
            CreateAccountServicePages();
            CreateSettingsNavigation();
            pageTransitionTimer.Tick += AnimateSettingsPageTransition;
            FormClosed += (_, _) => pageTransitionTimer.Stop();
            Font = new Font("Segoe UI Variable Text", 9.5F, FontStyle.Regular, GraphicsUnit.Point);
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
            BindHotkeyBox(txtHotkeyAutoLoginAll, manifest.AutoLoginAllHotkey);
            BindHotkeyBox(txtHotkeyConfirmations, manifest.ConfirmationsHotkey);

            ApplyLocalization();
            RefreshCloudLastSyncInfo();

            if (!DesktopDeviceAccess.CanManageLocalVault)
            {
                groupCloud.Enabled = false;
                groupCredentials.Enabled = false;
            }
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

        private void CreateActionHotkeyControls()
        {
            txtHotkeyAutoLoginAll.ReadOnly = true;
            txtHotkeyConfirmations.ReadOnly = true;
            txtHotkeyAutoLoginAll.KeyDown += txtHotkey_KeyDown;
            txtHotkeyConfirmations.KeyDown += txtHotkey_KeyDown;
            groupQr.Controls.Add(lblHotkeyAutoLoginAll);
            groupQr.Controls.Add(txtHotkeyAutoLoginAll);
            groupQr.Controls.Add(lblHotkeyConfirmations);
            groupQr.Controls.Add(txtHotkeyConfirmations);
        }

        private void CreateCredentialsControls()
        {
            chkAutoLoginExpired.AutoSize = true;
            chkAskBeforeAutoLogin.AutoSize = true;
            chkRequireCredentialsEncryption.AutoSize = true;
            chkAllowPlainCredentials.AutoSize = true;
            chkCredentialsCloudEnabled.AutoSize = true;
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

        private void CreateAppearanceControl()
        {
            btnAppearance.Click += btnAppearance_Click;
            Controls.Add(btnAppearance);
        }

        private void CreateAppearancePage()
        {
            lblAppearanceIntro.AutoSize = false;
            ConfigureThemeChoice(btnThemeGraphite, UiThemePreset.Graphite);
            ConfigureThemeChoice(btnThemeObsidian, UiThemePreset.Obsidian);
            ConfigureThemeChoice(btnThemeSlate, UiThemePreset.Slate);
            panelAppearancePage.Controls.AddRange(new Control[]
            {
                lblAppearanceTitle, lblAppearanceIntro,
                btnThemeGraphite, btnThemeObsidian, btnThemeSlate,
                lblThemeGraphite, lblThemeObsidian, lblThemeSlate
            });
        }

        private void CreateAccountServicePages()
        {
            lblMonitoringDescription.AutoSize = false;
            lblMobileDescription.AutoSize = false;
            btnOpenMonitoring.Click += (_, _) => openAccountMonitoring?.Invoke(this);
            btnOpenMobilePairing.Click += (_, _) => openMobilePairing?.Invoke(this);
            btnOpenCredentialsManager.Click += (_, _) => openCredentialsManager?.Invoke(this);
            groupMonitoring.Controls.AddRange(new Control[] { lblMonitoringDescription, btnOpenMonitoring });
            groupMobile.Controls.AddRange(new Control[] { lblMobileDescription, btnOpenMobilePairing });
            panelMonitoringPage.Controls.Add(groupMonitoring);
            panelMobilePage.Controls.Add(groupMobile);
            groupCredentials.Controls.Add(btnOpenCredentialsManager);
        }

        private void ConfigureThemeChoice(Button button, UiThemePreset preset)
        {
            button.Tag = preset;
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderSize = 0;
            button.Cursor = Cursors.Hand;
            button.Paint += PaintThemePreview;
            button.Click += (_, _) => SelectThemePreset(preset);
        }

        private void SelectThemePreset(UiThemePreset preset)
        {
            manifest.UiThemePreset = preset;
            manifest.Save();
            Branding.ApplyAppearance(manifest);
            ApplyTheme();
            ModernUi.RefreshTheme(this);
            Invalidate(true);
        }

        private void UpdateThemeSelection()
        {
            btnThemeGraphite.Invalidate();
            btnThemeObsidian.Invalidate();
            btnThemeSlate.Invalidate();
        }

        private void PaintThemePreview(object sender, PaintEventArgs e)
        {
            if (sender is not Button button || button.Tag is not UiThemePreset preset)
            {
                return;
            }

            Color top = preset switch
            {
                UiThemePreset.Obsidian => Color.FromArgb(8, 10, 15),
                UiThemePreset.Slate => Color.FromArgb(35, 45, 60),
                _ => Color.FromArgb(23, 28, 38)
            };
            Color bottom = preset switch
            {
                UiThemePreset.Obsidian => Color.FromArgb(20, 24, 34),
                UiThemePreset.Slate => Color.FromArgb(52, 64, 84),
                _ => Color.FromArgb(38, 45, 59)
            };
            bool selected = manifest != null && manifest.UiThemePreset == preset;
            Rectangle bounds = new Rectangle(0, 0, button.Width - 1, button.Height - 1);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using LinearGradientBrush background = new LinearGradientBrush(bounds, top, bottom, 90F);
            using GraphicsPath path = CreatePreviewPath(bounds, 14);
            using Pen border = new Pen(selected ? Branding.Accent : Color.FromArgb(120, Branding.Outline), selected ? 2F : 1F);
            e.Graphics.FillPath(background, path);
            e.Graphics.DrawPath(border, path);

            using SolidBrush glow = new SolidBrush(Color.FromArgb(selected ? 82 : 36, Branding.Accent));
            e.Graphics.FillEllipse(glow, button.Width - 88, -28, 118, 94);
            using Font previewFont = new Font("Segoe UI Semibold", 11F, FontStyle.Bold);
            TextRenderer.DrawText(
                e.Graphics,
                button.Text,
                previewFont,
                new Rectangle(16, 68, button.Width - 32, 30),
                Color.White,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }

        private static GraphicsPath CreatePreviewPath(Rectangle rectangle, int radius)
        {
            int diameter = radius * 2;
            GraphicsPath path = new GraphicsPath();
            path.AddArc(rectangle.Left, rectangle.Top, diameter, diameter, 180, 90);
            path.AddArc(rectangle.Right - diameter, rectangle.Top, diameter, diameter, 270, 90);
            path.AddArc(rectangle.Right - diameter, rectangle.Bottom - diameter, diameter, diameter, 0, 90);
            path.AddArc(rectangle.Left, rectangle.Bottom - diameter, diameter, diameter, 90, 90);
            path.CloseFigure();
            return path;
        }

        private void CreateSettingsNavigation()
        {
            panelSettingsNav.BackColor = Branding.WindowBackground;
            panelGeneralPage.BackColor = Branding.WindowBackground;
            panelQrPage.BackColor = Branding.WindowBackground;
            panelCloudPage.BackColor = Branding.WindowBackground;
            panelCredentialsPage.BackColor = Branding.WindowBackground;
            panelAppearancePage.BackColor = Branding.WindowBackground;
            panelMonitoringPage.BackColor = Branding.WindowBackground;
            panelMobilePage.BackColor = Branding.WindowBackground;
            panelSettingsNav.Tag = "workspace-surface";
            panelSettingsNav.Paint += PaintSettingsNavigation;
            lblSettingsNavTitle.Font = new Font("Segoe UI Variable Display Semibold", 12F, FontStyle.Bold, GraphicsUnit.Point);
            lblSettingsNavTitle.ForeColor = Branding.HeadingText;
            panelGeneralPage.Tag = "workspace-surface";
            panelQrPage.Tag = "workspace-surface";
            panelCloudPage.Tag = "workspace-surface";
            panelCredentialsPage.Tag = "workspace-surface";
            panelAppearancePage.Tag = "workspace-surface";
            panelMonitoringPage.Tag = "workspace-surface";
            panelMobilePage.Tag = "workspace-surface";

            ConfigurePageButton(btnPageGeneral, panelGeneralPage);
            ConfigurePageButton(btnPageQr, panelQrPage);
            ConfigurePageButton(btnPageCloud, panelCloudPage);
            ConfigurePageButton(btnPageCredentials, panelCredentialsPage);
            ConfigurePageButton(btnPageAppearance, panelAppearancePage);
            ConfigurePageButton(btnPageMonitoring, panelMonitoringPage);
            ConfigurePageButton(btnPageMobile, panelMobilePage);

            panelSettingsNav.Controls.AddRange(new Control[]
            {
                lblSettingsNavTitle,
                btnPageGeneral, btnPageAppearance, btnPageQr, btnPageCloud,
                btnPageCredentials, btnPageMonitoring, btnPageMobile
            });
            Controls.AddRange(new Control[]
            {
                panelSettingsNav, panelGeneralPage, panelAppearancePage, panelQrPage, panelCloudPage,
                panelCredentialsPage, panelMonitoringPage, panelMobilePage
            });
        }

        private void ConfigurePageButton(Button button, Panel page)
        {
            button.Click += (_, _) => ShowSettingsPage(page);
            button.TextAlign = ContentAlignment.MiddleLeft;
            button.Padding = new Padding(14, 0, 0, 0);
        }

        private void ShowSettingsPage(Panel page)
        {
            Panel[] pages =
            {
                panelGeneralPage, panelAppearancePage, panelQrPage, panelCloudPage,
                panelCredentialsPage, panelMonitoringPage, panelMobilePage
            };
            Button[] buttons =
            {
                btnPageGeneral, btnPageAppearance, btnPageQr, btnPageCloud,
                btnPageCredentials, btnPageMonitoring, btnPageMobile
            };
            Panel[] buttonPages =
            {
                panelGeneralPage, panelAppearancePage, panelQrPage, panelCloudPage,
                panelCredentialsPage, panelMonitoringPage, panelMobilePage
            };
            for (int index = 0; index < buttons.Length; index++)
            {
                bool selected = buttonPages[index] == page;
                buttons[index].ForeColor = selected ? Branding.HeadingText : Branding.MutedText;
                ModernUi.StyleNavigationButton(buttons[index], selected);
            }

            if (activeSettingsPage == page)
            {
                page.Visible = true;
                page.Location = new Point(SettingsPageLeft, SettingsPageTop);
            }
            else if (activeSettingsPage == null || !IsHandleCreated || !Visible)
            {
                pageTransitionTimer.Stop();
                foreach (Panel candidate in pages)
                {
                    candidate.Visible = candidate == page;
                    candidate.Location = new Point(SettingsPageLeft, SettingsPageTop);
                }
                activeSettingsPage = page;
            }
            else
            {
                StartSettingsPageTransition(page, pages);
            }

            page.BringToFront();
            btnSave.BringToFront();
        }

        private void StartSettingsPageTransition(Panel page, Panel[] pages)
        {
            pageTransitionTimer.Stop();
            foreach (Panel candidate in pages)
            {
                if (candidate != activeSettingsPage && candidate != page)
                {
                    candidate.Visible = false;
                    candidate.Location = new Point(SettingsPageLeft, SettingsPageTop);
                }
            }

            transitionFromPage = activeSettingsPage;
            transitionToPage = page;
            transitionFromPage.Location = new Point(SettingsPageLeft, SettingsPageTop);
            transitionFromPage.Visible = true;
            transitionToPage.Location = new Point(SettingsPageLeft + 28, SettingsPageTop);
            transitionToPage.Visible = true;
            transitionToPage.BringToFront();
            activeSettingsPage = page;
            pageTransitionWatch.Restart();
            pageTransitionTimer.Start();
        }

        private void AnimateSettingsPageTransition(object sender, EventArgs e)
        {
            if (transitionFromPage == null || transitionToPage == null)
            {
                pageTransitionTimer.Stop();
                return;
            }

            float time = Math.Min(1F, pageTransitionWatch.ElapsedMilliseconds / (float)PageTransitionDurationMs);
            float eased = 1F - (float)Math.Pow(1F - time, 3D);
            transitionToPage.Left = SettingsPageLeft + (int)Math.Round(28F * (1F - eased));
            transitionFromPage.Left = SettingsPageLeft - (int)Math.Round(12F * eased);
            transitionToPage.Invalidate();

            if (time < 1F)
            {
                return;
            }

            pageTransitionTimer.Stop();
            pageTransitionWatch.Stop();
            transitionFromPage.Visible = false;
            transitionFromPage.Location = new Point(SettingsPageLeft, SettingsPageTop);
            transitionToPage.Location = new Point(SettingsPageLeft, SettingsPageTop);
            transitionFromPage = null;
            transitionToPage = null;
            btnSave.BringToFront();
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
        }

        private void ApplyTheme()
        {
            Panel activePage = new[]
            {
                panelGeneralPage, panelAppearancePage, panelQrPage, panelCloudPage,
                panelCredentialsPage, panelMonitoringPage, panelMobilePage
            }
                .FirstOrDefault(page => page.Visible) ?? panelGeneralPage;
            BackColor = Branding.WindowBackground;
            ForeColor = Branding.HeadingText;

            ModernUi.ApplyGlassCard(groupQr, Localizer.Choose("QR Login and Hotkeys", "QR-вход и хоткеи"));
            ModernUi.ApplyGlassCard(groupCloud, Localizer.Choose("Cloud Sync", "Облачная синхронизация"));
            ModernUi.ApplyGlassCard(groupCredentials, Localizer.Choose("Credentials and Auto Login", "Логины и автовход"));
            ModernUi.ApplyGlassCard(groupMonitoring, Localizer.Choose("Account monitoring", "Мониторинг аккаунтов"));
            ModernUi.ApplyGlassCard(groupMobile, Localizer.Choose("Connect SDA++ Mobile", "Подключить SDA++ Mobile"));
            ModernUi.ApplyGlassCard(groupGeneralPreferences, Localizer.Choose("Application", "Приложение"));
            ModernUi.ApplyGlassCard(groupConfirmationAutomation, Localizer.Choose("Confirmation automation", "Автоматизация подтверждений"));
            ModernUi.ApplyGlassCard(groupMonitoring, Localizer.Choose("Account monitoring", "Мониторинг аккаунтов"));
            ModernUi.ApplyGlassCard(groupMobile, Localizer.Choose("Connect SDA++ Mobile", "Подключить SDA++ Mobile"));
            labelLanguage.ForeColor = Branding.MutedText;
            label1.ForeColor = Branding.MutedText;

            foreach (Button button in Controls.OfType<Button>()
                .Concat(panelGeneralPage.Controls.OfType<Button>())
                .Concat(groupQr.Controls.OfType<Button>())
                .Concat(groupCloud.Controls.OfType<Button>())
                .Concat(groupCredentials.Controls.OfType<Button>())
                .Concat(groupGeneralPreferences.Controls.OfType<Button>())
                .Concat(groupConfirmationAutomation.Controls.OfType<Button>())
                .Concat(groupMonitoring.Controls.OfType<Button>())
                .Concat(groupMobile.Controls.OfType<Button>())
                .Concat(panelAppearancePage.Controls.OfType<Button>())
                .Where(button => button.Tag is not UiThemePreset))
            {
                button.FlatStyle = FlatStyle.Flat;
                button.FlatAppearance.BorderSize = 1;
                button.FlatAppearance.BorderColor = Branding.Outline;
                button.BackColor = Branding.AccentSoft;
                button.ForeColor = Branding.HeadingText;
                button.Font = new Font("Segoe UI Semibold", 9F, FontStyle.Regular, GraphicsUnit.Point);
                ModernUi.RoundButton(button, false);
            }

            foreach (Button button in panelSettingsNav.Controls.OfType<Button>())
            {
                ModernUi.StyleNavigationButton(button, false);
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
                .Concat(panelGeneralPage.Controls.OfType<ComboBox>())
                .Concat(groupQr.Controls.OfType<ComboBox>())
                .Concat(groupCloud.Controls.OfType<ComboBox>())
                .Concat(groupCredentials.Controls.OfType<ComboBox>())
                .Concat(groupGeneralPreferences.Controls.OfType<ComboBox>()))
            {
                ModernUi.RoundComboBox(comboBox);
            }

            foreach (NumericUpDown numeric in groupConfirmationAutomation.Controls.OfType<NumericUpDown>()
                .Concat(groupQr.Controls.OfType<NumericUpDown>()))
            {
                ModernUi.RoundNumericUpDown(numeric);
            }

            foreach (Label label in Controls.OfType<Label>()
                .Concat(panelGeneralPage.Controls.OfType<Label>())
                .Concat(groupQr.Controls.OfType<Label>())
                .Concat(groupCloud.Controls.OfType<Label>())
                .Concat(groupCredentials.Controls.OfType<Label>())
                .Concat(groupGeneralPreferences.Controls.OfType<Label>())
                .Concat(groupConfirmationAutomation.Controls.OfType<Label>())
                .Concat(groupMonitoring.Controls.OfType<Label>())
                .Concat(groupMobile.Controls.OfType<Label>())
                .Concat(panelAppearancePage.Controls.OfType<Label>()))
            {
                if (label == lblCloudStatus || label == lblCloudLastSyncValue)
                {
                    continue;
                }

                label.ForeColor = Branding.MutedText;
            }

            foreach (CheckBox checkBox in Controls.OfType<CheckBox>()
                .Concat(panelGeneralPage.Controls.OfType<CheckBox>())
                .Concat(groupQr.Controls.OfType<CheckBox>())
                .Concat(groupCloud.Controls.OfType<CheckBox>())
                .Concat(groupCredentials.Controls.OfType<CheckBox>())
                .Concat(groupConfirmationAutomation.Controls.OfType<CheckBox>()))
            {
                checkBox.ForeColor = Branding.HeadingText;
                checkBox.Font = new Font("Segoe UI Variable Text", 9.25F, FontStyle.Regular, GraphicsUnit.Point);
                ModernUi.StyleToggleRow(checkBox);
            }

            numPeriodicInterval.BackColor = Branding.AccentDark;
            numPeriodicInterval.ForeColor = Branding.HeadingText;
            numCursorScanSize.BackColor = Branding.AccentDark;
            numCursorScanSize.ForeColor = Branding.HeadingText;
            WindowEffects.ApplyDarkControlTheme(numPeriodicInterval);
            WindowEffects.ApplyDarkControlTheme(numCursorScanSize);
            lblCloudStatus.ForeColor = Branding.MutedText;
            lblGeneralTitle.ForeColor = Branding.HeadingText;
            lblAppearanceTitle.ForeColor = Branding.HeadingText;
            lblAppearanceIntro.ForeColor = Branding.MutedText;
            UpdateThemeSelection();
            ShowSettingsPage(activePage);
        }

        private void ApplyCompactLayout()
        {
            if (compactLayoutApplied)
            {
                return;
            }

            compactLayoutApplied = true;
            ClientSize = new Size(920, 700);
            MinimumSize = new Size(840, 620);

            panelSettingsNav.SetBounds(12, 52, 204, 580);
            lblSettingsNavTitle.SetBounds(18, 14, 168, 26);
            btnPageGeneral.SetBounds(10, 52, 184, 40);
            btnPageAppearance.SetBounds(10, 100, 184, 40);
            btnPageQr.SetBounds(10, 148, 184, 40);
            btnPageCloud.SetBounds(10, 196, 184, 40);
            btnPageCredentials.SetBounds(10, 244, 184, 40);
            btnPageMonitoring.SetBounds(10, 292, 184, 40);
            btnPageMobile.SetBounds(10, 340, 184, 40);

            Panel[] pages =
            {
                panelGeneralPage, panelAppearancePage, panelQrPage, panelCloudPage,
                panelCredentialsPage, panelMonitoringPage, panelMobilePage
            };
            foreach (Panel page in pages)
            {
                page.SetBounds(228, 52, 680, 580);
                page.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            }

            panelGeneralPage.Controls.AddRange(new Control[] { lblGeneralTitle, groupGeneralPreferences, groupConfirmationAutomation });
            groupGeneralPreferences.Controls.AddRange(new Control[] { labelLanguage, cmbLanguage });
            groupConfirmationAutomation.Controls.AddRange(new Control[]
            {
                chkPeriodicChecking, numPeriodicInterval, label1,
                chkCheckAll, chkConfirmMarket, chkConfirmTrades
            });
            panelQrPage.Controls.Add(groupQr);
            panelCloudPage.Controls.Add(groupCloud);
            panelCredentialsPage.Controls.Add(groupCredentials);
            LayoutAppearancePage();
            LayoutAccountServicePages();

            lblGeneralTitle.Font = new Font("Segoe UI Variable Display Semibold", 18F, FontStyle.Bold, GraphicsUnit.Point);
            lblGeneralTitle.ForeColor = Branding.HeadingText;
            lblGeneralTitle.SetBounds(12, 12, 400, 32);

            groupGeneralPreferences.SetBounds(12, 54, 640, 104);
            groupConfirmationAutomation.SetBounds(12, 174, 640, 274);

            labelLanguage.Location = new Point(18, 42);
            cmbLanguage.Location = new Point(112, 38);
            cmbLanguage.Size = new Size(220, 28);

            chkPeriodicChecking.Location = new Point(18, 48);
            chkPeriodicChecking.Size = new Size(600, 50);

            numPeriodicInterval.Location = new Point(18, 112);
            numPeriodicInterval.Size = new Size(62, 24);
            label1.SetBounds(136, 108, 458, 42);
            label1.AutoSize = false;

            chkCheckAll.Location = new Point(18, 164);
            chkCheckAll.Size = new Size(600, 34);
            chkConfirmMarket.Location = new Point(18, 200);
            chkConfirmMarket.Size = new Size(600, 34);
            chkConfirmTrades.Location = new Point(18, 236);
            chkConfirmTrades.Size = new Size(600, 34);

            groupQr.Location = new Point(0, 0);
            groupQr.Size = new Size(652, 580);
            groupQr.Padding = new Padding(14, 28, 14, 14);
            LayoutQrGroup();

            groupCredentials.Location = new Point(0, 0);
            groupCredentials.Size = new Size(652, 580);
            groupCredentials.Padding = new Padding(14, 28, 14, 14);
            LayoutCredentialsGroup();

            groupCloud.Location = new Point(0, 0);
            groupCloud.Size = new Size(652, 580);
            groupCloud.Padding = new Padding(14, 28, 14, 14);
            LayoutCloudGroup();

            btnSave.Location = new Point(728, 648);
            btnSave.Size = new Size(180, 38);
            btnSave.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            ShowSettingsPage(panelGeneralPage);
        }

        private void LayoutAppearancePage()
        {
            lblAppearanceTitle.Font = new Font("Segoe UI Variable Display Semibold", 18F, FontStyle.Bold, GraphicsUnit.Point);
            lblAppearanceTitle.SetBounds(12, 12, 500, 32);
            lblAppearanceIntro.SetBounds(12, 48, 612, 42);

            Button[] previews = { btnThemeGraphite, btnThemeObsidian, btnThemeSlate };
            Label[] labels = { lblThemeGraphite, lblThemeObsidian, lblThemeSlate };
            for (int index = 0; index < previews.Length; index++)
            {
                int x = 12 + (index * 204);
                previews[index].SetBounds(x, 108, 188, 118);
                labels[index].SetBounds(x, 234, 188, 42);
                labels[index].TextAlign = ContentAlignment.TopCenter;
            }

            btnAppearance.SetBounds(12, 306, 600, 40);
            panelAppearancePage.Controls.Add(btnAppearance);
        }

        private void LayoutAccountServicePages()
        {
            groupMonitoring.SetBounds(12, 12, 640, 220);
            lblMonitoringDescription.SetBounds(20, 48, 596, 62);
            btnOpenMonitoring.SetBounds(20, 132, 240, 40);

            groupMobile.SetBounds(12, 12, 640, 220);
            lblMobileDescription.SetBounds(20, 48, 596, 62);
            btnOpenMobilePairing.SetBounds(20, 132, 240, 40);

            btnOpenCredentialsManager.SetBounds(18, 438, 280, 40);
        }

        private void PaintSettingsNavigation(object sender, PaintEventArgs e)
        {
            if (sender is not Panel panel || panel.Width < 2 || panel.Height < 2) return;
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle bounds = new Rectangle(0, 0, panel.Width - 1, panel.Height - 1);
            using GraphicsPath path = CreatePreviewPath(bounds, 18);
            using LinearGradientBrush fill = new LinearGradientBrush(
                bounds,
                Color.FromArgb(225, Branding.CardBackground),
                Color.FromArgb(190, Branding.AccentDark),
                90F);
            using Pen outline = new Pen(Color.FromArgb(105, Branding.Outline));
            e.Graphics.FillPath(fill, path);
            e.Graphics.DrawPath(outline, path);
        }

        private void LayoutQrGroup()
        {
            label3.Location = new Point(18, 44);
            label3.AutoSize = false;
            label3.Size = new Size(298, 18);
            txtHotkeyToggle.Location = new Point(18, 64);
            txtHotkeyToggle.Size = new Size(292, 28);

            label4.Location = new Point(328, 44);
            label4.AutoSize = false;
            label4.Size = new Size(298, 18);
            txtHotkeyScan.Location = new Point(328, 64);
            txtHotkeyScan.Size = new Size(306, 28);

            label5.Location = new Point(18, 104);
            label5.AutoSize = false;
            label5.Size = new Size(298, 18);
            txtHotkeyPrev.Location = new Point(18, 124);
            txtHotkeyPrev.Size = new Size(292, 28);

            label6.Location = new Point(328, 104);
            label6.AutoSize = false;
            label6.Size = new Size(298, 18);
            txtHotkeyNext.Location = new Point(328, 124);
            txtHotkeyNext.Size = new Size(306, 28);

            lblHotkeyAutoLoginAll.Location = new Point(18, 164);
            lblHotkeyAutoLoginAll.Size = new Size(298, 18);
            txtHotkeyAutoLoginAll.Location = new Point(18, 184);
            txtHotkeyAutoLoginAll.Size = new Size(292, 28);

            lblHotkeyConfirmations.Location = new Point(328, 164);
            lblHotkeyConfirmations.Size = new Size(298, 18);
            txtHotkeyConfirmations.Location = new Point(328, 184);
            txtHotkeyConfirmations.Size = new Size(306, 28);

            label7.Location = new Point(18, 224);
            cmbQrCaptureMode.Location = new Point(18, 244);
            cmbQrCaptureMode.Size = new Size(616, 28);

            lblCursorScanSize.SetBounds(18, 286, 360, 20);
            lblCursorScanSize.AutoSize = false;
            numCursorScanSize.Location = new Point(18, 310);
            numCursorScanSize.Size = new Size(92, 24);
            btnResetHotkeys.Location = new Point(122, 307);
            btnResetHotkeys.Size = new Size(112, 30);

            chkEnableQrHotkeys.Location = new Point(18, 356);
            chkEnableQrHotkeys.Size = new Size(616, 36);
        }

        private void LayoutCloudGroup()
        {
            lblCloudProvider.Location = new Point(18, 42);
            lblCloudProvider.AutoSize = true;
            cmbCloudProvider.Location = new Point(18, 62);
            cmbCloudProvider.Size = new Size(616, 28);

            label8.Location = new Point(18, 102);
            txtWebDavUrl.Location = new Point(18, 122);
            txtWebDavUrl.Size = new Size(616, 28);

            label9.Location = new Point(18, 162);
            txtWebDavUsername.Location = new Point(18, 182);
            txtWebDavUsername.Size = new Size(616, 28);

            label10.Location = new Point(18, 222);
            txtWebDavPassword.Location = new Point(18, 242);
            txtWebDavPassword.Size = new Size(616, 28);

            lblCloudExtra.Location = new Point(18, 282);
            lblCloudExtra.AutoSize = true;
            txtCloudExtra.Location = new Point(18, 302);
            txtCloudExtra.Size = new Size(616, 28);

            label11.Location = new Point(18, 342);
            txtWebDavPath.Location = new Point(18, 362);
            txtWebDavPath.Size = new Size(616, 28);

            btnOneDriveSignIn.Location = new Point(18, 182);
            btnOneDriveSignIn.Size = new Size(616, 32);

            chkSyncStoredCredentials.Location = new Point(18, 402);
            chkSyncStoredCredentials.Size = new Size(616, 36);

            btnCloudTest.Location = new Point(18, 438);
            btnCloudTest.Size = new Size(194, 32);
            btnCloudPull.Location = new Point(221, 438);
            btnCloudPull.Size = new Size(194, 32);
            btnCloudPush.Location = new Point(424, 438);
            btnCloudPush.Size = new Size(210, 32);

            btnCloudOpenBackups.Location = new Point(18, 478);
            btnCloudOpenBackups.Size = new Size(616, 32);

            lblCloudStatus.Location = new Point(18, 518);
            lblCloudStatus.Size = new Size(616, 24);
            lblCloudLastSyncTitle.SetBounds(18, 548, 210, 24);
            lblCloudLastSyncValue.Location = new Point(228, 548);
            lblCloudLastSyncValue.Size = new Size(406, 24);
        }

        private void LayoutCredentialsGroup()
        {
            chkAutoLoginExpired.Location = new Point(18, 44);
            chkAutoLoginExpired.Size = new Size(616, 34);

            chkAskBeforeAutoLogin.Location = new Point(18, 78);
            chkAskBeforeAutoLogin.Size = new Size(616, 34);

            lblCredentialsPath.Location = new Point(18, 120);
            lblCredentialsPath.Size = new Size(320, 18);
            txtCredentialsPath.Location = new Point(18, 142);
            txtCredentialsPath.Size = new Size(470, 28);
            btnBrowseCredentialsPath.Location = new Point(500, 140);
            btnBrowseCredentialsPath.Size = new Size(134, 32);

            chkRequireCredentialsEncryption.Location = new Point(18, 188);
            chkRequireCredentialsEncryption.Size = new Size(616, 34);

            chkAllowPlainCredentials.Location = new Point(18, 222);
            chkAllowPlainCredentials.Size = new Size(616, 34);

            chkCredentialsCloudEnabled.Location = new Point(18, 256);
            chkCredentialsCloudEnabled.Size = new Size(616, 34);

            lblCredentialsCloudProvider.Location = new Point(18, 300);
            lblCredentialsCloudProvider.Size = new Size(300, 18);
            cmbCredentialsCloudProvider.Location = new Point(18, 322);
            cmbCredentialsCloudProvider.Size = new Size(616, 28);

            lblCredentialsCloudPath.Location = new Point(18, 362);
            lblCredentialsCloudPath.Size = new Size(300, 18);
            txtCredentialsCloudPath.Location = new Point(18, 384);
            txtCredentialsCloudPath.Size = new Size(616, 28);
        }

        private void ApplyLocalization()
        {
            ModernUi.ApplyGlassCard(groupQr, Localizer.Choose("QR Login and Hotkeys", "QR-вход и хоткеи"));
            ModernUi.ApplyGlassCard(groupCloud, Localizer.Choose("Cloud Sync", "Облачная синхронизация"));
            ModernUi.ApplyGlassCard(groupCredentials, Localizer.Choose("Credentials and Auto Login", "Логины и автовход"));
            Text = Localizer.Choose("Settings", "Настройки");
            lblSettingsNavTitle.Text = "SDA++";
            lblGeneralTitle.Text = Localizer.Choose("General settings", "Общие настройки");
            btnPageGeneral.Text = Localizer.Choose("General", "Общие");
            btnPageQr.Text = Localizer.Choose("QR and hotkeys", "QR и хоткеи");
            btnPageCloud.Text = Localizer.Choose("Cloud sync", "Облако");
            btnPageCredentials.Text = Localizer.Choose("Login credentials", "Логины и пароли");
            btnPageMonitoring.Text = Localizer.Choose("Account monitoring", "Мониторинг");
            btnPageMobile.Text = Localizer.Choose("Connect Mobile", "Подключить Mobile");
            btnPageAppearance.Text = Localizer.Choose("Appearance", "Оформление");
            lblAppearanceTitle.Text = Localizer.Choose("Appearance", "Оформление");
            lblAppearanceIntro.Text = Localizer.Choose(
                "Choose a visual direction. You can fine-tune the accent and density afterwards.",
                "Выберите стиль интерфейса. Затем можно настроить акцент и плотность элементов.");
            btnThemeGraphite.Text = "Graphite";
            btnThemeObsidian.Text = "Obsidian";
            btnThemeSlate.Text = "Slate";
            lblThemeGraphite.Text = Localizer.Choose("Balanced graphite\nwith a blue accent", "Сбалансированный графит\nс синим акцентом");
            lblThemeObsidian.Text = Localizer.Choose("Deep black\nwith crisp contrast", "Глубокий черный\nс четким контрастом");
            lblThemeSlate.Text = Localizer.Choose("Softer slate\nwith lighter surfaces", "Мягкий сланцевый\nсо светлыми поверхностями");
            lblMonitoringDescription.Text = Localizer.Choose(
                "Review all accounts in one workspace: session health, VAC status, Steam level, games and tradeable inventory.",
                "Проверяйте все аккаунты в одном окне: состояние сессий, VAC, уровень Steam, игры и доступный для обмена инвентарь.");
            btnOpenMonitoring.Text = Localizer.Choose("Open account monitoring", "Открыть мониторинг");
            lblMobileDescription.Text = Localizer.Choose(
                "Pair SDA++ Mobile, manage trusted-device access and transfer only the accounts you choose.",
                "Подключайте SDA++ Mobile, управляйте доверенными устройствами и передавайте только выбранные аккаунты.");
            btnOpenMobilePairing.Text = Localizer.Choose("Connect SDA++ Mobile", "Подключить SDA++ Mobile");
            btnOpenCredentialsManager.Text = Localizer.Choose("Manage saved credentials", "Управление сохраненными логинами");
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
            label3.Text = Localizer.Choose("QR mode:", "Режим QR:");
            label4.Text = Localizer.Choose("Scan QR:", "Скан QR:");
            label5.Text = Localizer.Choose("Previous account:", "Предыдущий аккаунт:");
            label6.Text = Localizer.Choose("Next account:", "Следующий аккаунт:");
            lblHotkeyAutoLoginAll.Text = Localizer.Choose("Auto-login all:", "Автовход для всех:");
            lblHotkeyConfirmations.Text = Localizer.Choose("Open confirmations:", "Открыть подтверждения:");
            label7.Text = Localizer.Choose("QR capture source:", "Источник захвата QR:");
            lblCursorScanSize.Text = Localizer.Choose("Cursor area size (pixels):", "Размер области у курсора (пикс.):");
            btnResetHotkeys.Text = Localizer.Choose("Reset defaults", "Сбросить");
            btnAppearance.Text = Localizer.Choose("Customize accent and density", "Настроить акцент и плотность");
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

            int folderLabelY;
            if (dropbox || oneDrive || googleDrive)
            {
                folderLabelY = 222;
            }
            else if (webDav)
            {
                folderLabelY = 282;
            }
            else
            {
                folderLabelY = 342;
            }

            int syncToggleY = folderLabelY + 60;
            int actionButtonsY = syncToggleY + 36;
            label11.Location = new Point(18, folderLabelY);
            SetCloudControlLocation(txtWebDavPath, new Point(18, folderLabelY + 20));
            chkSyncStoredCredentials.Location = new Point(18, syncToggleY);
            btnCloudTest.Location = new Point(18, actionButtonsY);
            btnCloudPull.Location = new Point(226, actionButtonsY);
            btnCloudPush.Location = new Point(434, actionButtonsY);
            btnCloudOpenBackups.Location = new Point(18, actionButtonsY + 40);
            lblCloudStatus.Location = new Point(18, actionButtonsY + 80);
            lblCloudLastSyncTitle.Location = new Point(18, actionButtonsY + 110);
            lblCloudLastSyncValue.Location = new Point(228, actionButtonsY + 110);

            btnOneDriveSignIn.Location = new Point(18, 182);
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
                { "Next account", GetBindingFromBox(txtHotkeyNext, Keys.Right) },
                { "Auto login all", GetBindingFromBox(txtHotkeyAutoLoginAll, Keys.L) },
                { "Confirmations", GetBindingFromBox(txtHotkeyConfirmations, Keys.C) }
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
            manifest.AutoLoginAllHotkey = bindings["Auto login all"];
            manifest.ConfirmationsHotkey = bindings["Confirmations"];
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

        private void btnAppearance_Click(object sender, EventArgs e)
        {
            using AppearanceSettingsForm appearanceForm = new AppearanceSettingsForm(manifest);
            appearanceForm.ShowDialog(this);
            manifest = Manifest.GetManifest(true);
            Branding.ApplyAppearance(manifest);
            ApplyTheme();
            ModernUi.RefreshTheme(this);
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
            BindHotkeyBox(txtHotkeyAutoLoginAll, HotkeyBindingHelper.CreateDefault(Keys.L));
            BindHotkeyBox(txtHotkeyConfirmations, HotkeyBindingHelper.CreateDefault(Keys.C));
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
            await RunCloudActionAsync(Localizer.Choose("Pulling from cloud...", "Загрузка из облака..."), async () =>
            {
                CloudSyncService.CloudPullResult pullResult = null;
                await UseCloudProvidersAsync(async (provider, credentialsProvider) =>
                {
                    CloudSyncService.CloudPullPlan plan = await cloudSyncService.PreparePullAsync(
                        provider,
                        chkSyncStoredCredentials.Checked,
                        credentialsProvider);
                    string summary = Localizer.Choose(
                        $"Cloud vault validated successfully.\n\nAccounts in cloud: {plan.RemoteAccountCount}\nAccounts currently local: {plan.LocalAccountCount}\nVault encrypted: {(plan.IsEncrypted ? "Yes" : "No")}\nCredentials included: {(plan.IncludesCredentials ? "Yes" : "No")}\n\nA local backup will be created before any file is replaced. Continue?",
                        $"Облачное хранилище успешно проверено.\n\nАккаунтов в облаке: {plan.RemoteAccountCount}\nАккаунтов локально сейчас: {plan.LocalAccountCount}\nХранилище зашифровано: {(plan.IsEncrypted ? "Да" : "Нет")}\nЛогины включены: {(plan.IncludesCredentials ? "Да" : "Нет")}\n\nПеред заменой файлов будет создана локальная резервная копия. Продолжить?");
                    DialogResult confirmation = MessageBox.Show(
                        summary,
                        Localizer.Choose("Validated Cloud Pull", "Проверенная загрузка из облака"),
                        MessageBoxButtons.OKCancel,
                        MessageBoxIcon.Warning);
                    if (confirmation == DialogResult.OK)
                    {
                        pullResult = cloudSyncService.CommitPull(plan);
                    }
                });

                if (pullResult == null)
                {
                    lblCloudStatus.Text = Localizer.Choose("Pull canceled. No local files were changed.", "Загрузка отменена. Локальные файлы не изменены.");
                    return;
                }

                manifest = Manifest.GetManifest(true);
                UpdateCloudLastSync("pull", true);
                lblCloudStatus.Text = Localizer.Choose(
                    $"Pull completed safely. {pullResult.AccountCount} accounts restored. Backup: {pullResult.BackupDirectory}",
                    $"Безопасная загрузка завершена. Восстановлено аккаунтов: {pullResult.AccountCount}. Резервная копия: {pullResult.BackupDirectory}");
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
