using System;
using System.Diagnostics;
using System.Windows.Forms;
using SteamAuth;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Net;
using Newtonsoft.Json;
using System.Threading;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace Steam_Desktop_Authenticator
{
    public partial class MainForm : Form
    {
        private SteamGuardAccount currentAccount = null;
        private SteamGuardAccount[] allAccounts;
        private List<SteamGuardAccount> filteredAccounts = new List<SteamGuardAccount>();
        private List<string> updatedSessions = new List<string>();
        private Manifest manifest;
        private static SemaphoreSlim confirmationsSemaphore = new SemaphoreSlim(1, 1);
        private readonly SteamQrLoginService qrLoginService = new SteamQrLoginService();
        private readonly StoredCredentialLoginService storedCredentialLoginService = new StoredCredentialLoginService();
        private readonly HotkeyOverlayForm hotkeyOverlay = new HotkeyOverlayForm();
        private readonly Label lblAccountName = new Label();
        private readonly Label lblAccountHint = new Label();
        private readonly Label lblSessionBadge = new Label();
        private readonly Label lblSessionDetails = new Label();
        private readonly Button btnQuickLoginAgain = new Button();
        private readonly Button btnQuickTerminate = new Button();
        private readonly Button btnFavoriteAccount = new Button();
        private readonly Panel panelAccountActions = new Panel();
        private readonly Panel panelTimeoutTrack = new Panel();
        private readonly Panel panelTimeoutFill = new Panel();
        private readonly Panel panelTopNav = new Panel();
        private readonly Button btnNavFile = new Button();
        private readonly Button btnNavAccount = new Button();
        private readonly LinkLabel lblFooterKofi = new LinkLabel();
        private readonly ToolStripMenuItem menuManageCredentials = new ToolStripMenuItem();
        private readonly ToolStripMenuItem menuAutoLoginAllAccounts = new ToolStripMenuItem();

        private const int WM_HOTKEY = 0x0312;
        private const uint MOD_ALT = 0x0001;
        private const uint MOD_CONTROL = 0x0002;
        private const uint MOD_SHIFT = 0x0004;
        private const uint MOD_NOREPEAT = 0x4000;
        private const int HOTKEY_TOGGLE_QR = 1;
        private const int HOTKEY_SCAN_QR = 2;
        private const int HOTKEY_PREVIOUS_ACCOUNT = 3;
        private const int HOTKEY_NEXT_ACCOUNT = 4;

        private long steamTime = 0;
        private long currentSteamChunk = 0;
        private string passKey = null;
        private bool startSilent = false;
        private bool qrHotkeysEnabled = false;
        private bool qrScanInProgress = false;
        private bool suppressQrToggleEvents = false;
        private int timeoutBarValue = 0;
        private int timeoutBarMax = 30;
        private bool startupBatchAutoLoginAttempted = false;

        // Forms
        private TradePopupForm popupFrm = new TradePopupForm();

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        public MainForm()
        {
            InitializeComponent();
            Icon = Branding.LoadAppIcon();
            trayIcon.Icon = Icon;
            accountToolStripMenuItem.DropDownItems.Insert(1, menuManageCredentials);
            accountToolStripMenuItem.DropDownItems.Insert(2, menuAutoLoginAllAccounts);
            menuManageCredentials.Click += menuManageCredentials_Click;
            menuAutoLoginAllAccounts.Click += menuAutoLoginAllAccounts_Click;
            DoubleBuffered = true;
            BuildModernLayout();
            ModernUi.AttachWindowChrome(this, true, false);
            ApplyTheme();
            ApplyLocalization();
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            WindowEffects.ApplyModernChrome(this);
        }

        public void SetEncryptionKey(string key)
        {
            passKey = key;
        }

        public void StartSilent(bool silent)
        {
            startSilent = silent;
        }

        private void BuildModernLayout()
        {
            int topOffset = ModernUi.HeaderHeight + 8;

            Text = Branding.FullAppName;
            ClientSize = new Size(392, 668);
            MinimumSize = new Size(408, 520);

            menuStrip.Visible = false;
            panelTopNav.Location = new Point(12, topOffset + 6);
            panelTopNav.Size = new Size(366, 28);
            panelTopNav.BackColor = Color.Transparent;
            Controls.Add(panelTopNav);
            panelTopNav.BringToFront();

            ConfigureNavButton(btnNavFile, 0, 112);
            ConfigureNavButton(btnNavAccount, 122, 146);
            panelTopNav.Controls.Add(btnNavFile);
            panelTopNav.Controls.Add(btnNavAccount);

            panelButtons.Location = new Point(12, topOffset + 40);
            panelButtons.Size = new Size(366, 34);
            chkEnableQrHotkeys.Location = new Point(16, topOffset + 84);
            chkEnableQrHotkeys.Text = "";
            chkEnableQrHotkeys.ForeColor = Branding.MutedText;

            groupBox1.Location = new Point(12, topOffset + 112);
            groupBox1.Size = new Size(366, 126);
            // Set the card padding before positioning its children. Changing it later
            // makes WinForms offset the anchored controls and clips the timeout bar.
            groupBox1.Padding = new Padding(14, 28, 14, 14);
            txtLoginToken.Location = new Point(14, 34);
            txtLoginToken.Size = new Size(286, 34);
            btnCopy.Location = new Point(308, 34);
            btnCopy.Size = new Size(44, 34);
            pbTimeout.Location = new Point(14, 82);
            pbTimeout.Size = new Size(338, 16);
            groupAccount.Location = new Point(12, topOffset + 240);
            groupAccount.Size = new Size(366, 142);
            listAccounts.Location = new Point(12, topOffset + 408);
            listAccounts.Size = new Size(366, 154);
            txtAccSearch.Location = new Point(58, topOffset + 566);
            txtAccSearch.Size = new Size(320, 22);
            label1.Location = new Point(12, topOffset + 570);
            labelUpdate.Location = new Point(12, topOffset + 604);
            labelUpdate.Size = new Size(52, 16);
            labelVersion.Location = new Point(252, topOffset + 622);
            labelVersion.Size = new Size(126, 16);
            lblStatus.Location = new Point(226, topOffset + 85);
            lblStatus.Size = new Size(152, 18);
            lblFooterKofi.Location = new Point(70, topOffset + 604);
            lblFooterKofi.Size = new Size(44, 16);
            lblFooterKofi.BackColor = Color.Transparent;
            labelUpdate.TextAlign = ContentAlignment.MiddleLeft;
            lblFooterKofi.TextAlign = ContentAlignment.MiddleLeft;
            lblFooterKofi.LinkClicked -= lblFooterKofi_LinkClicked;
            lblFooterKofi.LinkClicked += lblFooterKofi_LinkClicked;
            if (!Controls.Contains(lblFooterKofi))
            {
                Controls.Add(lblFooterKofi);
            }

            groupAccount.Text = "";

            lblAccountName.Font = new Font("Segoe UI Semibold", 11.5F, FontStyle.Bold, GraphicsUnit.Point);
            lblAccountName.ForeColor = Branding.HeadingText;
            lblAccountName.Location = new Point(14, 22);
            lblAccountName.Size = new Size(220, 22);
            lblAccountName.Text = "";

            btnFavoriteAccount.Size = new Size(104, 26);
            btnFavoriteAccount.Location = new Point(246, 20);
            btnFavoriteAccount.Text = "";
            btnFavoriteAccount.Click += btnFavoriteAccount_Click;

            lblSessionBadge.Font = new Font("Segoe UI Semibold", 8.5F, FontStyle.Bold, GraphicsUnit.Point);
            lblSessionBadge.Location = new Point(14, 49);
            lblSessionBadge.Size = new Size(112, 22);
            lblSessionBadge.TextAlign = ContentAlignment.MiddleCenter;

            lblSessionDetails.Font = new Font("Segoe UI", 8.75F, FontStyle.Regular, GraphicsUnit.Point);
            lblSessionDetails.ForeColor = Branding.MutedText;
            lblSessionDetails.Location = new Point(132, 50);
            lblSessionDetails.Size = new Size(222, 38);

            lblAccountHint.Font = new Font("Segoe UI", 8.5F, FontStyle.Regular, GraphicsUnit.Point);
            lblAccountHint.ForeColor = Branding.MutedText;
            lblAccountHint.Location = new Point(14, 92);
            lblAccountHint.Size = new Size(340, 16);
            lblAccountHint.Visible = false;

            panelAccountActions.Location = new Point(14, 98);
            panelAccountActions.Size = new Size(334, 28);
            panelAccountActions.BackColor = Color.Transparent;

            btnTradeConfirmations.Location = new Point(0, 0);
            btnTradeConfirmations.Size = new Size(102, 28);
            btnTradeConfirmations.Text = "";

            btnQuickLoginAgain.Size = new Size(102, 28);
            btnQuickLoginAgain.Location = new Point(116, 0);
            btnQuickLoginAgain.Text = "";
            btnQuickLoginAgain.Click += btnQuickLoginAgain_Click;

            btnQuickTerminate.Size = new Size(102, 28);
            btnQuickTerminate.Location = new Point(232, 0);
            btnQuickTerminate.Text = "";
            btnQuickTerminate.Click += btnQuickTerminate_Click;

            panelTimeoutTrack.Location = pbTimeout.Location;
            panelTimeoutTrack.Size = pbTimeout.Size;
            panelTimeoutTrack.Anchor = pbTimeout.Anchor;
            panelTimeoutTrack.Margin = Padding.Empty;
            groupBox1.Controls.Add(panelTimeoutTrack);
            panelTimeoutTrack.BringToFront();
            panelTimeoutTrack.Paint += panelTimeoutTrack_Paint;
            panelTimeoutTrack.Resize += panelTimeoutTrack_Resize;

            groupAccount.Controls.Add(lblAccountName);
            groupAccount.Controls.Add(btnFavoriteAccount);
            groupAccount.Controls.Add(lblSessionBadge);
            groupAccount.Controls.Add(lblSessionDetails);
            groupAccount.Controls.Add(lblAccountHint);
            groupAccount.Controls.Add(panelAccountActions);
            panelAccountActions.Controls.Add(btnTradeConfirmations);
            panelAccountActions.Controls.Add(btnQuickLoginAgain);
            panelAccountActions.Controls.Add(btnQuickTerminate);

            listAccounts.DrawMode = DrawMode.OwnerDrawFixed;
            listAccounts.ItemHeight = 18;
            listAccounts.DrawItem += listAccounts_DrawItem;

            btnSteamLogin.Text = "";
            btnScanQrLogin.Text = "";
            btnManageEncryption.Text = "";
            btnCopy.Text = "";
            groupBox1.Text = "";
            accountToolStripMenuItem.Text = "";
            trayTradeConfirmations.Text = "";
            trayCopySteamGuard.Text = "";
            label1.Text = "";
      labelUpdate.Text = "";
      lblFooterKofi.Text = "";
      LayoutActionButtons();
        }

        private void ConfigureNavButton(Button button, int x, int width)
        {
            button.Location = new Point(x, 0);
            button.Size = new Size(width, 24);
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderSize = 0;
            button.FlatAppearance.MouseOverBackColor = Branding.AccentSoft;
            button.FlatAppearance.MouseDownBackColor = Color.FromArgb(110, Branding.AccentSoft);
            button.BackColor = Branding.AccentDark;
            button.ForeColor = Branding.MutedText;
            button.Font = new Font("Segoe UI Semibold", 8F, FontStyle.Regular, GraphicsUnit.Point);
            button.UseVisualStyleBackColor = false;
        }

        private void ApplyLocalization()
        {
            fileToolStripMenuItem.Text = Localizer.Choose("File", "Файл");
            btnNavFile.Text = fileToolStripMenuItem.Text;
            menuImportAccount.Text = Localizer.Choose("Import account", "Импорт аккаунта");
            menuSettings.Text = Localizer.Choose("Settings", "Настройки");
            menuQuit.Text = Localizer.Choose("Quit", "Выход");
            accountToolStripMenuItem.Text = Localizer.Choose("Account tools", "Инструменты аккаунта");
            menuManageCredentials.Text = Localizer.Choose("Manage login credentials", "Управление логинами");
            menuAutoLoginAllAccounts.Text = Localizer.Choose("Auto login all accounts", "Автовход для всех аккаунтов");
            btnNavAccount.Text = accountToolStripMenuItem.Text;
            menuLoginAgain.Text = Localizer.Choose("Login again", "Войти заново");
            menuTerminateSessions.Text = Localizer.Choose("Terminate all sessions", "Завершить все сессии");
            menuRemoveAccountFromManifest.Text = Localizer.Choose("Remove from manifest", "Убрать из manifest");
            menuDeactivateAuthenticator.Text = Localizer.Choose("Deactivate authenticator", "Отключить аутентификатор");
            trayRestore.Text = Localizer.Choose("Restore", "Открыть");
            trayTradeConfirmations.Text = Localizer.Choose("Open confirmations", "Открыть подтверждения");
            trayCopySteamGuard.Text = Localizer.Choose("Copy current code", "Скопировать текущий код");
            trayQuit.Text = Localizer.Choose("Quit", "Выход");
            chkEnableQrHotkeys.Text = Localizer.Choose("QR hotkeys ready for quick approvals", "QR-хоткеи готовы для быстрого подтверждения");
            groupAccount.Tag = Localizer.Choose("Selected account", "Выбранный аккаунт");
            btnTradeConfirmations.Text = Localizer.Choose("Confirmations", "Подтверждения");
            btnQuickLoginAgain.Text = Localizer.Choose("Login again", "Войти заново");
            btnQuickTerminate.Text = Localizer.Choose("End sessions", "Завершить сессии");
            btnSteamLogin.Text = Localizer.Choose("Add Account", "Добавить аккаунт");
            btnScanQrLogin.Text = Localizer.Choose("Scan QR", "Скан QR");
            btnManageEncryption.Text = Localizer.Choose("Vault", "Шифрование");
            btnCopy.Text = Localizer.Choose("Copy", "Копировать");
            ModernUi.ApplyGlassCard(groupBox1, Localizer.Choose("Steam Guard code", "Код Steam Guard"));
            ModernUi.ApplyGlassCard(groupAccount, Localizer.Choose("Selected account", "Выбранный аккаунт"));
            label1.Text = Localizer.Choose("Find:", "Поиск:");
            labelUpdate.Text = "GitHub";
            lblFooterKofi.Text = "Ko-fi";
            UpdateSelectedAccountCard();
        }

        private void ApplyTheme()
        {
            BackColor = Branding.WindowBackground;
            ForeColor = Branding.HeadingText;
            menuStrip.ForeColor = Branding.HeadingText;
            menuStrip.Renderer = new GraphiteMenuRenderer();
            menuStripTray.Renderer = new GraphiteMenuRenderer();
            menuStrip.BackColor = Branding.AccentDark;
            menuStripTray.BackColor = Branding.CardBackground;
            menuStripTray.ForeColor = Branding.HeadingText;
            menuStripTray.ShowImageMargin = false;
            menuStripTray.Padding = new Padding(8, 8, 8, 8);
            menuStripTray.Font = new Font("Segoe UI", 9.25F, FontStyle.Regular, GraphicsUnit.Point);
            lblStatus.BackColor = Branding.AccentDark;

            btnNavFile.Click -= btnNavFile_Click;
            btnNavFile.Click += btnNavFile_Click;
            btnNavAccount.Click -= btnNavAccount_Click;
            btnNavAccount.Click += btnNavAccount_Click;

            ModernUi.ApplyGlassCard(groupBox1, Localizer.Choose("Steam Guard code", "Код Steam Guard"));
            ModernUi.ApplyGlassCard(groupAccount, Localizer.Choose("Selected account", "Выбранный аккаунт"));
            StyleButton(btnSteamLogin, true);
            StyleButton(btnScanQrLogin, false);
            StyleButton(btnManageEncryption, false);
            StyleButton(btnCopy, false);
            StyleButton(btnTradeConfirmations, false);
            StyleButton(btnQuickLoginAgain, false);
            StyleButton(btnQuickTerminate, false);
            StyleButton(btnFavoriteAccount, false);

            txtLoginToken.BackColor = Branding.AccentDark;
              txtLoginToken.ForeColor = Branding.HeadingText;
              txtLoginToken.BorderStyle = BorderStyle.FixedSingle;
              pbTimeout.Visible = false;
      panelTimeoutTrack.BackColor = Color.Transparent;
        panelButtons.BackColor = Color.Transparent;
            ModernUi.WrapListBox(listAccounts);
            ModernUi.WrapTextBox(txtAccSearch);
            ModernUi.WrapTextBox(txtLoginToken, 10, 8);
            labelVersion.ForeColor = Branding.MutedText;
            labelVersion.Font = new Font("Segoe UI", 6.9F, FontStyle.Regular, GraphicsUnit.Point);
            labelUpdate.LinkColor = Branding.Accent;
            labelUpdate.ActiveLinkColor = Color.White;
            labelUpdate.VisitedLinkColor = Branding.Accent;
            labelUpdate.Font = new Font("Segoe UI", 7.2F, FontStyle.Regular, GraphicsUnit.Point);
            lblFooterKofi.LinkColor = Branding.Accent;
            lblFooterKofi.ActiveLinkColor = Color.White;
            lblFooterKofi.VisitedLinkColor = Branding.Accent;
            lblFooterKofi.Font = new Font("Segoe UI", 7.2F, FontStyle.Regular, GraphicsUnit.Point);
            label1.ForeColor = Branding.MutedText;
            chkEnableQrHotkeys.ForeColor = Branding.MutedText;
            lblStatus.ForeColor = Branding.MutedText;
            btnNavFile.ForeColor = Branding.MutedText;
            btnNavAccount.ForeColor = Branding.MutedText;

            ConfigureTrayMenuTheme();
        }

        private void LayoutActionButtons()
        {
            int count = panelButtons.Controls.OfType<Button>().Count();
            if (count == 0)
            {
                return;
            }

            int spacing = 8;
            int buttonHeight = 28;
            int buttonWidth = (panelButtons.Width - (spacing * (count - 1))) / count;
            int x = 0;

            foreach (Button button in panelButtons.Controls.OfType<Button>())
            {
                button.SetBounds(x, 3, buttonWidth, buttonHeight);
                x += buttonWidth + spacing;
            }
        }

        private void btnNavFile_Click(object sender, EventArgs e)
        {
            btnNavFile.ForeColor = Branding.HeadingText;
            btnNavAccount.ForeColor = Branding.MutedText;
            fileToolStripMenuItem.DropDown.Show(btnNavFile, new Point(0, btnNavFile.Height + 6));
        }

        private void btnNavAccount_Click(object sender, EventArgs e)
        {
            btnNavAccount.ForeColor = Branding.HeadingText;
            btnNavFile.ForeColor = Branding.MutedText;
            accountToolStripMenuItem.DropDown.Show(btnNavAccount, new Point(0, btnNavAccount.Height + 6));
        }

        private void ConfigureTrayMenuTheme()
        {
            foreach (ToolStripItem item in menuStripTray.Items)
            {
                item.ForeColor = Branding.HeadingText;
                item.BackColor = Branding.CardBackground;

                if (item is ToolStripMenuItem menuItem)
                {
                    menuItem.Margin = new Padding(0, 2, 0, 2);
                    menuItem.Padding = new Padding(10, 7, 10, 7);
                }
            }

            trayAccountList.BackColor = Branding.AccentDark;
            trayAccountList.ForeColor = Branding.HeadingText;
            trayAccountList.FlatStyle = FlatStyle.Flat;
            trayAccountList.Margin = new Padding(0, 4, 0, 6);
            trayAccountList.Padding = new Padding(8, 4, 8, 4);
            trayAccountList.ComboBox.DrawMode = DrawMode.OwnerDrawFixed;
            trayAccountList.ComboBox.DropDownStyle = ComboBoxStyle.DropDownList;
            trayAccountList.ComboBox.FlatStyle = FlatStyle.Flat;
            trayAccountList.ComboBox.BackColor = Branding.AccentDark;
            trayAccountList.ComboBox.ForeColor = Branding.HeadingText;
            trayAccountList.ComboBox.Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
            trayAccountList.ComboBox.DrawItem -= trayAccountList_DrawItem;
            trayAccountList.ComboBox.DrawItem += trayAccountList_DrawItem;
        }

        private void trayAccountList_DrawItem(object sender, DrawItemEventArgs e)
        {
            e.DrawBackground();

            Rectangle bounds = e.Bounds;
            Color backColor = (e.State & DrawItemState.Selected) == DrawItemState.Selected
                ? Branding.AccentSoft
                : Branding.AccentDark;
            Color textColor = Branding.HeadingText;

            using (SolidBrush backBrush = new SolidBrush(backColor))
            using (SolidBrush textBrush = new SolidBrush(textColor))
            {
                e.Graphics.FillRectangle(backBrush, bounds);

                string text = string.Empty;
                if (e.Index >= 0 && e.Index < trayAccountList.Items.Count)
                {
                    text = trayAccountList.Items[e.Index]?.ToString() ?? string.Empty;
                }
                else if (sender is ComboBox comboBox)
                {
                    text = comboBox.Text;
                }

                Rectangle textBounds = new Rectangle(bounds.X + 8, bounds.Y + 2, bounds.Width - 16, bounds.Height - 4);
                TextRenderer.DrawText(e.Graphics, text, trayAccountList.ComboBox.Font, textBounds, textColor, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            }

            e.DrawFocusRectangle();
        }

        private static void StyleButton(Button button, bool primary)
        {
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderSize = 1;
            button.Font = new Font("Segoe UI Semibold", 8.25F, FontStyle.Bold, GraphicsUnit.Point);

            if (primary)
            {
                button.BackColor = Branding.Accent;
                button.ForeColor = Color.White;
                button.FlatAppearance.BorderColor = Branding.Accent;
            }
            else
            {
                button.BackColor = Branding.AccentSoft;
                button.ForeColor = Branding.HeadingText;
                button.FlatAppearance.BorderColor = Branding.Outline;
            }

            ModernUi.RoundButton(button, primary);
        }

        // Form event handlers

        private async void MainForm_Shown(object sender, EventArgs e)
        {
            this.labelVersion.Text = String.Format("{0} v{1}", Branding.AppName, Application.ProductVersion);
            try
            {
                this.manifest = Manifest.GetManifest();
            }
            catch (ManifestParseException)
            {
                MessageBox.Show("Не удалось прочитать настройки. Попробуйте перезапустить " + Branding.AppName + ".", Branding.FullAppName, MessageBoxButtons.OK, MessageBoxIcon.Error);
                this.Close();
            }

            // Make sure we don't show that welcome dialog again
            this.manifest.FirstRun = false;
            this.manifest.Save();

            // Tick first time manually to sync time
            timerSteamGuard_Tick(new object(), EventArgs.Empty);

            if (manifest.Encrypted)
            {
                if (passKey == null)
                {
                    passKey = manifest.PromptForPassKey();
                    if (passKey == null)
                    {
                        Application.Exit();
                    }
                }

                    btnManageEncryption.Text = Localizer.Choose("Manage encryption", "Управление шифрованием");
            }
            else
            {
                    btnManageEncryption.Text = Localizer.Choose("Set up encryption", "Настроить шифрование");
            }

            btnManageEncryption.Enabled = manifest.Entries.Count > 0;

            loadSettings();
            RegisterGlobalHotkeys();
            loadAccountsList();

            checkForUpdates();
            UpdateSelectedAccountCard();

            await MaybeAutoLoginAllAccountsOnStartupAsync();

            if (startSilent)
            {
                this.WindowState = FormWindowState.Minimized;
            }
        }

        private void MainForm_Load(object sender, EventArgs e)
        {
            trayIcon.Icon = this.Icon;
            trayIcon.Text = Branding.FullAppName;
            RegisterGlobalHotkeys();
        }

        private void MainForm_Resize(object sender, EventArgs e)
        {
            if (this.WindowState == FormWindowState.Minimized)
            {
                this.Hide();
            }
        }

        private void MainForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            UnregisterGlobalHotkeys();
            hotkeyOverlay.Close();
            Application.Exit();
        }


        // UI Button handlers

        private void btnSteamLogin_Click(object sender, EventArgs e)
        {
            var loginForm = new LoginForm();
            loginForm.ShowDialog();
            this.loadAccountsList();
        }

        private void btnTradeConfirmations_Click(object sender, EventArgs e)
        {
            if (currentAccount == null) return;

            string oText = btnTradeConfirmations.Text;
                btnTradeConfirmations.Text = Localizer.Choose("Loading...", "Загрузка...");
            btnTradeConfirmations.Text = oText;

            ConfirmationFormWeb confirms = new ConfirmationFormWeb(currentAccount);
            confirms.Show();
        }

        private void btnManageEncryption_Click(object sender, EventArgs e)
        {
            if (manifest.Encrypted)
            {
                InputForm currentPassKeyForm = new InputForm("Enter current passkey", true);
                currentPassKeyForm.ShowDialog();

                if (currentPassKeyForm.Canceled)
                {
                    return;
                }

                string curPassKey = currentPassKeyForm.txtBox.Text;

                InputForm changePassKeyForm = new InputForm("Enter new passkey, or leave blank to remove encryption.");
                changePassKeyForm.ShowDialog();

                if (changePassKeyForm.Canceled && !string.IsNullOrEmpty(changePassKeyForm.txtBox.Text))
                {
                    return;
                }

                InputForm changePassKeyForm2 = new InputForm("Confirm new passkey, or leave blank to remove encryption.");
                changePassKeyForm2.ShowDialog();

                if (changePassKeyForm2.Canceled && !string.IsNullOrEmpty(changePassKeyForm.txtBox.Text))
                {
                    return;
                }

                string newPassKey = changePassKeyForm.txtBox.Text;
                string confirmPassKey = changePassKeyForm2.txtBox.Text;

                if (newPassKey != confirmPassKey)
                {
                MessageBox.Show("Ключи шифрования не совпадают.");
                    return;
                }

                if (newPassKey.Length == 0)
                {
                    newPassKey = null;
                }

                string action = newPassKey == null ? "remove" : "change";
                if (!manifest.ChangeEncryptionKey(curPassKey, newPassKey))
                {
                MessageBox.Show("Не удалось " + (action == "change" ? "изменить" : "установить") + " ключ шифрования.");
                }
                else
                {
                MessageBox.Show(action == "change" ? "Ключ шифрования успешно изменен." : "Ключ шифрования успешно установлен.");
                    this.loadAccountsList();
                }
            }
            else
            {
                passKey = manifest.PromptSetupPassKey();
                this.loadAccountsList();
            }
        }

        private void labelUpdate_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            Process.Start(new ProcessStartInfo(Branding.GithubUrl) { UseShellExecute = true });
        }

        private void lblFooterKofi_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            Process.Start(new ProcessStartInfo(Branding.KofiUrl) { UseShellExecute = true });
        }

        private void btnCopy_Click(object sender, EventArgs e)
        {
            CopyLoginToken();
        }

        private async void btnScanQrLogin_Click(object sender, EventArgs e)
        {
            await RunQrScanAsync(true);
        }

        private void btnQuickLoginAgain_Click(object sender, EventArgs e)
        {
            if (currentAccount == null)
            {
                return;
            }

            _ = HandleLoginAgainRequestAsync();
        }

        private async void btnQuickTerminate_Click(object sender, EventArgs e)
        {
            await Task.Yield();
            menuTerminateSessions_Click(sender, e);
        }

        private void btnFavoriteAccount_Click(object sender, EventArgs e)
        {
            ToggleCurrentAccountFavorite();
        }


        // Tool strip menu handlers

        private void menuQuit_Click(object sender, EventArgs e)
        {
            Application.Exit();
        }

        private void menuRemoveAccountFromManifest_Click(object sender, EventArgs e)
        {
            if (manifest.Encrypted)
            {
                MessageBox.Show(
                    Localizer.Choose(
                        "You cannot remove accounts from the manifest while it is encrypted.",
                        "Нельзя удалять аккаунты из manifest, пока он зашифрован."),
                    Localizer.Choose("Remove from manifest", "Удаление из manifest"),
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
            else
            {
                DialogResult res = MessageBox.Show(
                    Localizer.Choose(
                        "This will remove the selected account from the manifest.\nUse this when you want to move the maFile to another computer.\nThe maFile itself will not be deleted.",
                        "Это удалит выбранный аккаунт из файла manifest.\nИспользуйте это, чтобы перенести maFile на другой компьютер.\nСам maFile удален не будет."),
                    Localizer.Choose("Remove from manifest", "Удаление из manifest"),
                    MessageBoxButtons.OKCancel);
                if (res == DialogResult.OK)
                {
                    manifest.RemoveAccount(currentAccount, false);
                    MessageBox.Show(
                        Localizer.Choose(
                            "The account was removed from the manifest.\nYou can now move its maFile to another computer and import it from the File menu.",
                            "Аккаунт удален из manifest.\nТеперь вы можете перенести его maFile на другой компьютер и импортировать через меню «Файл»."),
                        Localizer.Choose("Remove from manifest", "Удаление из manifest"));
                    loadAccountsList();
                }
            }
        }

        private void menuLoginAgain_Click(object sender, EventArgs e)
        {
            _ = HandleLoginAgainRequestAsync();
        }

        private void menuManageCredentials_Click(object sender, EventArgs e)
        {
            using CredentialsManagerForm form = new CredentialsManagerForm();
            form.ShowDialog(this);
            manifest = Manifest.GetManifest(true);
            loadAccountsList();
            ApplyAccountFilter(currentAccount?.AccountName);
        }

        private void menuAutoLoginAllAccounts_Click(object sender, EventArgs e)
        {
            _ = RunAutoLoginForAllAccountsAsync(true, true, Localizer.Choose("Auto login all accounts", "Автовход для всех аккаунтов"));
        }

        private async void menuTerminateSessions_Click(object sender, EventArgs e)
        {
            if (currentAccount == null)
            {
                return;
            }

            DialogResult result = MessageBox.Show(
                Localizer.Choose(
                    "This will terminate all saved Steam sessions for the selected account, including the current SDA++ session.\nAfter that, you may need to use \"Login again\".\n\nContinue?",
                    "Это завершит все сохраненные Steam-сессии для выбранного аккаунта, включая текущую сессию SDA++.\nПосле этого может понадобиться «Войти заново».\n\nПродолжить?"),
                Localizer.Choose("Terminate all sessions", "Завершение всех сессий"),
                MessageBoxButtons.OKCancel,
                MessageBoxIcon.Warning);

            if (result != DialogResult.OK)
            {
                return;
            }

            try
            {
                lblStatus.Text = Localizer.Choose("Ending sessions...", "Завершение сессий...");
                await qrLoginService.TerminateAllSessionsAsync(currentAccount);

                currentAccount.Session.AccessToken = null;
                currentAccount.Session.RefreshToken = null;
                currentAccount.Session.SessionID = null;
                manifest.SaveAccount(currentAccount, manifest.Encrypted, passKey);

                lblStatus.Text = "";
                StoredCredentialLoginService.RestoreResult restoreResult = await TryRestoreStoredSessionAsync(currentAccount, "Terminate All Sessions", true);
                if (restoreResult.Success)
                {
                    MessageBox.Show(
                        Localizer.Choose(
                            "All Steam sessions were terminated, and SDA++ automatically restored this account for its own session.",
                            "Все Steam-сессии были завершены, и SDA++ автоматически восстановил этот аккаунт для своей собственной сессии."),
                        Localizer.Choose("Terminate all sessions", "Завершение всех сессий"),
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);
                }
                else if (restoreResult.CredentialsAvailable)
                {
                    MessageBox.Show(
                        Localizer.Choose(
                            "All Steam sessions were terminated. SDA++ could not restore this account automatically.\n\n",
                            "Все Steam-сессии были завершены. SDA++ не смог автоматически восстановить этот аккаунт.\n\n")
                        + restoreResult.Message
                        + Localizer.Choose(
                            "\n\nUse \"Login again\" if needed.",
                            "\n\nПри необходимости используйте «Войти заново»."),
                        Localizer.Choose("Terminate all sessions", "Завершение всех сессий"),
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                }
                else
                {
                    MessageBox.Show(
                        Localizer.Choose(
                            "All Steam sessions were terminated. No saved credentials were found for this account, so SDA++ also remained signed out.\n\nUse \"Login again\" if you want this session to be restored automatically next time.",
                            "Все Steam-сессии были завершены. Для этого аккаунта не найдено сохраненных учетных данных, поэтому SDA++ тоже остался без входа.\n\nИспользуйте «Войти заново», если хотите, чтобы в следующий раз сессия восстанавливалась автоматически."),
                        Localizer.Choose("Terminate all sessions", "Завершение всех сессий"),
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);
                }
            }
            catch (Exception ex)
            {
                lblStatus.Text = "";
                MessageBox.Show(
                    GetFullExceptionMessage(ex),
                    Localizer.Choose("Terminate all sessions", "Завершение всех сессий"),
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }

        private void menuImportAccount_Click(object sender, EventArgs e)
        {
            ImportAccountForm currentImport_maFile_Form = new ImportAccountForm();
            currentImport_maFile_Form.ShowDialog();
            loadAccountsList();
        }

        private void menuSettings_Click(object sender, EventArgs e)
        {
            new SettingsForm().ShowDialog();
            manifest = Manifest.GetManifest(true);
            Localizer.SetLanguage(manifest.UiLanguage);
            loadSettings();
            ApplyLocalization();
            loadAccountsList();
            RegisterGlobalHotkeys();
        }

        private async void menuDeactivateAuthenticator_Click(object sender, EventArgs e)
        {
            if (currentAccount == null) return;

            if (!await EnsureAccountSessionReadyAsync(currentAccount, "Deactivate Authenticator", true))
            {
                return;
            }

            DialogResult res = MessageBox.Show(
                Localizer.Choose(
                    "Do you want to fully remove Steam Guard?\nYes — completely remove Steam Guard.\nNo — switch back to email confirmations.",
                    "Вы хотите полностью удалить Steam Guard?\nДа — полностью удалить Steam Guard.\nНет — вернуться к подтверждениям по email."),
                Localizer.Choose("Deactivate authenticator", "Отключение аутентификатора") + ": " + currentAccount.AccountName,
                MessageBoxButtons.YesNoCancel);
            int scheme = 0;
            if (res == DialogResult.Yes)
            {
                scheme = 2;
            }
            else if (res == DialogResult.No)
            {
                scheme = 1;
            }
            else if (res == DialogResult.Cancel)
            {
                scheme = 0;
            }

            if (scheme != 0)
            {
                string confCode = currentAccount.GenerateSteamGuardCode();
                InputForm confirmationDialog = new InputForm(String.Format("Removing Steam Guard from {0}. Enter this confirmation code: {1}", currentAccount.AccountName, confCode));
                confirmationDialog.ShowDialog();

                if (confirmationDialog.Canceled)
                {
                    return;
                }

                string enteredCode = confirmationDialog.txtBox.Text.ToUpper();
                if (enteredCode != confCode)
                {
                    MessageBox.Show("Коды подтверждения не совпадают. Steam Guard не удален.");
                    return;
                }

                bool success = await currentAccount.DeactivateAuthenticator(scheme);
                if (success)
                {
                    MessageBox.Show(String.Format("Steam Guard {0}. maFile будет удален после нажатия OK. Если нужна резервная копия, сделайте ее сейчас.", (scheme == 2 ? "полностью удален" : "переключен на email")));
                    this.manifest.RemoveAccount(currentAccount);
                    this.loadAccountsList();
                }
                else
                {
                    MessageBox.Show("Не удалось отключить Steam Guard.");
                }
            }
            else
            {
                MessageBox.Show(
                    Localizer.Choose("Steam Guard was not removed. Action canceled.", "Steam Guard не был удален. Действие отменено."),
                    Localizer.Choose("Deactivate authenticator", "Отключение аутентификатора"),
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }
        }

        // Tray menu handlers
        private void trayIcon_MouseDoubleClick(object sender, MouseEventArgs e)
        {
            trayRestore_Click(sender, EventArgs.Empty);
        }

        private void trayRestore_Click(object sender, EventArgs e)
        {
            this.Show();
            this.WindowState = FormWindowState.Normal;
        }

        private void trayQuit_Click(object sender, EventArgs e)
        {
            Application.Exit();
        }

        private void trayTradeConfirmations_Click(object sender, EventArgs e)
        {
            btnTradeConfirmations_Click(sender, e);
        }

        private void trayCopySteamGuard_Click(object sender, EventArgs e)
        {
            if (txtLoginToken.Text != "")
            {
                Clipboard.SetText(txtLoginToken.Text);
            }
        }

        private void trayAccountList_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (trayAccountList.SelectedIndex >= 0 && trayAccountList.SelectedIndex < listAccounts.Items.Count)
            {
                listAccounts.SelectedIndex = trayAccountList.SelectedIndex;
            }
        }


        // Misc UI handlers
        private void listAccounts_SelectedValueChanged(object sender, EventArgs e)
        {
            int selectedIndex = listAccounts.SelectedIndex;
            if (selectedIndex < 0 || selectedIndex >= filteredAccounts.Count)
            {
                return;
            }

            SteamGuardAccount account = filteredAccounts[selectedIndex];
            trayAccountList.Text = account.AccountName;
            currentAccount = account;
            btnTradeConfirmations.Enabled = menuDeactivateAuthenticator.Enabled = menuTerminateSessions.Enabled = true;
            loadAccountInfo();
        }

        private void txtAccSearch_TextChanged(object sender, EventArgs e)
        {
            ApplyAccountFilter();
        }

        private void chkEnableQrHotkeys_CheckedChanged(object sender, EventArgs e)
        {
            if (suppressQrToggleEvents)
            {
                return;
            }

            SetQrHotkeysEnabled(chkEnableQrHotkeys.Checked, true, true);
        }


        // Timers

        private async void timerSteamGuard_Tick(object sender, EventArgs e)
        {
                lblStatus.Text = "Синхронизация времени со Steam...";
            steamTime = await TimeAligner.GetSteamTimeAsync();
            lblStatus.Text = "";

            currentSteamChunk = steamTime / 30L;
            int secondsUntilChange = (int)(steamTime - (currentSteamChunk * 30L));

            loadAccountInfo();
            if (currentAccount != null)
            {
                pbTimeout.Value = 30 - secondsUntilChange;
                UpdateTimeoutBar(30 - secondsUntilChange, 30);
            }
        }

        private async void timerTradesPopup_Tick(object sender, EventArgs e)
        {
            if (currentAccount == null || popupFrm.Visible) return;
            if (!confirmationsSemaphore.Wait(0))
            {
                return; //Only one thread may access this critical section at once. Mutex is a bad choice here because it'll cause a pileup of threads.
            }

            List<Confirmation> confs = new List<Confirmation>();
            Dictionary<SteamGuardAccount, List<Confirmation>> autoAcceptConfirmations = new Dictionary<SteamGuardAccount, List<Confirmation>>();

            SteamGuardAccount[] accs =
                manifest.CheckAllAccounts ? allAccounts : new SteamGuardAccount[] { currentAccount };

            try
            {
                lblStatus.Text = "Проверка подтверждений...";

                foreach (var acc in accs)
                {
                    if (!await EnsureAccountSessionReadyAsync(acc, "Trade Confirmations", true))
                    {
                        break;
                    }

                    try
                    {
                        Confirmation[] tmp = await acc.FetchConfirmationsAsync();
                        foreach (var conf in tmp)
                        {
                            if ((conf.ConfType == Confirmation.EMobileConfirmationType.MarketListing && manifest.AutoConfirmMarketTransactions) ||
                                (conf.ConfType == Confirmation.EMobileConfirmationType.Trade && manifest.AutoConfirmTrades))
                            {
                                if (!autoAcceptConfirmations.ContainsKey(acc))
                                    autoAcceptConfirmations[acc] = new List<Confirmation>();
                                autoAcceptConfirmations[acc].Add(conf);
                            }
                            else
                                confs.Add(conf);
                        }
                    }
                    catch (Exception)
                    {

                    }
                }

                lblStatus.Text = "";

                if (confs.Count > 0)
                {
                    popupFrm.Confirmations = confs.ToArray();
                    popupFrm.Popup();
                }
                if (autoAcceptConfirmations.Count > 0)
                {
                    foreach (var acc in autoAcceptConfirmations.Keys)
                    {
                        var confirmations = autoAcceptConfirmations[acc].ToArray();
                        await acc.AcceptMultipleConfirmations(confirmations);
                    }
                }
            }
            catch (SteamGuardAccount.WGTokenInvalidException)
            {
                lblStatus.Text = "";
            }

            confirmationsSemaphore.Release();
        }

        // Other methods

        private void CopyLoginToken()
        {
            string text = txtLoginToken.Text;
            if (String.IsNullOrEmpty(text))
                return;
            Clipboard.SetText(text);
        }

        private async Task RunQrScanAsync(bool interactive)
        {
            if (qrScanInProgress)
            {
                ShowOverlay(Localizer.Choose("Steam QR", "Steam QR"), Localizer.Choose("Scan already in progress.", "Сканирование уже выполняется."));
                return;
            }

            if (currentAccount == null)
            {
                if (interactive)
                {
                    MessageBox.Show(Localizer.Choose("Select the Steam account that should approve the QR login first.", "Сначала выберите Steam-аккаунт, который должен подтвердить QR-вход."), Localizer.Choose("Steam QR", "Steam QR"), MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                else
                {
                    ShowOverlay(Localizer.Choose("Steam QR", "Steam QR"), Localizer.Choose("Select an account first.", "Сначала выберите аккаунт."));
                }

                return;
            }

            if (!await EnsureAccountSessionReadyAsync(currentAccount, "Steam QR Login", true))
            {
                return;
            }

            string originalText = btnScanQrLogin.Text;
            qrScanInProgress = true;

            try
            {
                btnScanQrLogin.Enabled = false;
                btnScanQrLogin.Text = Localizer.Choose("Scanning...", "Сканирование...");
                lblStatus.Text = Localizer.Choose("Capturing screen...", "Захват экрана...");
                ShowOverlay(Localizer.Choose("Steam QR", "Steam QR"), Localizer.Choose("Scanning the screen...", "Сканирование экрана..."));

                string qrText = qrLoginService.CaptureAndDecodeSteamQr(manifest.QrCaptureMode, manifest.QrCursorScanSize);

                lblStatus.Text = Localizer.Choose("Approving QR login...", "Подтверждение QR-входа...");
                ShowOverlay(Localizer.Choose("Steam QR", "Steam QR"), Localizer.Choose("Approving login...", "Подтверждение входа..."));
                string result = await qrLoginService.HandleDecodedQrAsync(currentAccount, qrText);
                manifest.SaveAccount(currentAccount, manifest.Encrypted, passKey);

                lblStatus.Text = "";
                ShowOverlay(Localizer.Choose("Steam QR", "Steam QR"), result);

                if (interactive)
                {
                    MessageBox.Show(
                        result + "\n\n" + Localizer.Choose("Decoded value:", "Расшифрованное значение:") + "\n" + qrText,
                        Localizer.Choose("Steam QR", "Steam QR"),
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);
                }
            }
            catch (Exception ex)
            {
                lblStatus.Text = "";
                ShowOverlay(Localizer.Choose("Steam QR", "Steam QR"), GetInnermostExceptionMessage(ex));

                if (interactive)
                {
                    MessageBox.Show(GetFullExceptionMessage(ex), Localizer.Choose("Steam QR", "Steam QR"), MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
            finally
            {
                qrScanInProgress = false;
                btnScanQrLogin.Enabled = true;
                btnScanQrLogin.Text = originalText;
            }
        }

        /// <summary>
        /// Display a login form to the user to refresh their OAuth Token
        /// </summary>
        /// <param name="account">The account to refresh</param>
        private void PromptRefreshLogin(SteamGuardAccount account)
        {
            var loginForm = new LoginForm(LoginForm.LoginType.Refresh, account);
            loginForm.ShowDialog();
        }

        private async Task HandleLoginAgainRequestAsync()
        {
            if (currentAccount == null)
            {
                return;
            }

            bool hasStoredCredentials = storedCredentialLoginService.HasStoredCredentials(currentAccount.Session?.SteamID ?? 0, currentAccount.AccountName);
            bool expired = currentAccount.Session == null || currentAccount.Session.IsRefreshTokenExpired() || currentAccount.Session.IsAccessTokenExpired();
            if (expired && hasStoredCredentials && manifest.AutoLoginForExpiredSessions)
            {
                bool approved = !manifest.AskBeforeAutoLogin || MessageBox.Show(
                    Localizer.Choose(
                        "Saved credentials were found for this account.\n\nLogin again automatically now?",
                        "Для этого аккаунта найдены сохранённые логины.\n\nВыполнить автовход сейчас?"),
                    Localizer.Choose("Login again automatically", "Автовход"),
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question) == DialogResult.Yes;

                if (approved)
                {
                    StoredCredentialLoginService.RestoreResult restoreResult = await TryRestoreStoredSessionAsync(currentAccount, Localizer.Choose("Auto login", "Автовход"), true);
                    if (restoreResult.Success)
                    {
                        ShowOverlay(Localizer.Choose("Auto login", "Автовход"), Localizer.Choose("Auto login successful.", "Автовход выполнен успешно."));
                        return;
                    }

                    MessageBox.Show(
                        Localizer.Choose("Auto login failed.\n\n", "Автовход не удался.\n\n") + restoreResult.Message,
                        Localizer.Choose("Auto login failed", "Автовход не удался"),
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error);
                    return;
                }
            }

            PromptRefreshLogin(currentAccount);
            manifest = Manifest.GetManifest(true);
            loadAccountsList();
            ApplyAccountFilter(currentAccount.AccountName);
        }

        private bool ShouldAttemptAutoLogin(SteamGuardAccount account, string operationName, bool allowInteractivePrompt)
        {
            if (account?.Session == null || !manifest.AutoLoginForExpiredSessions)
            {
                return false;
            }

            bool hasCredentials = storedCredentialLoginService.HasStoredCredentials(account.Session.SteamID, account.AccountName);
            if (!hasCredentials)
            {
                return false;
            }

            if (!manifest.AskBeforeAutoLogin || !allowInteractivePrompt)
            {
                return true;
            }

            return MessageBox.Show(
                Localizer.Choose(
                    "Saved credentials were found for this account.\n\nSDA++ can try to log in again automatically before continuing.\n\nContinue?",
                    "Для этого аккаунта найдены сохранённые логины.\n\nSDA++ может попробовать войти автоматически перед продолжением.\n\nПродолжить?"),
                operationName,
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question) == DialogResult.Yes;
        }

        private async Task<bool> EnsureAccountSessionReadyAsync(SteamGuardAccount account, string operationName, bool allowInteractivePrompt)
        {
            if (account?.Session == null)
            {
                if (allowInteractivePrompt)
                {
            MessageBox.Show(Localizer.Choose("There is no active SDA++ session loaded for this account.", "Для этого аккаунта не загружена действующая сессия SDA++."), operationName, MessageBoxButtons.OK, MessageBoxIcon.Error);
                }

                return false;
            }

            if (account.Session.IsRefreshTokenExpired())
            {
                StoredCredentialLoginService.RestoreResult restoreResult = null;
                if (ShouldAttemptAutoLogin(account, operationName, allowInteractivePrompt))
                {
                    restoreResult = await TryRestoreStoredSessionAsync(account, operationName, false);
                    if (restoreResult.Success)
                    {
                        return true;
                    }
                }

                if (allowInteractivePrompt)
                {
                    bool hasStoredCredentials = storedCredentialLoginService.HasStoredCredentials(account.Session.SteamID, account.AccountName);
                    string message = (restoreResult?.CredentialsAvailable ?? hasStoredCredentials)
                        ? "Сессия истекла, и SDA++ не смог восстановить ее автоматически.\n\n" + (restoreResult?.Message ?? Localizer.Choose("Auto login failed.", "Автовход не удался.")) + "\n\nПожалуйста, войдите заново."
                        : "Сессия истекла, и для этого аккаунта не найдено сохраненных учетных данных.\n\nВойдите заново, если хотите, чтобы SDA++ мог автоматически восстанавливать ее в будущем.";
                    MessageBox.Show(message, operationName, MessageBoxButtons.OK, MessageBoxIcon.Information);
                    PromptRefreshLogin(account);
                }

                return account.Session != null && !account.Session.IsRefreshTokenExpired();
            }

            if (account.Session.IsAccessTokenExpired())
            {
                try
                {
            lblStatus.Text = Localizer.Choose("Refreshing session...", "Обновление сессии...");
                    await account.Session.RefreshAccessToken();
                    manifest.SaveAccount(account, manifest.Encrypted, passKey);
                    lblStatus.Text = "";
                    return true;
                }
                catch (Exception)
                {
                    lblStatus.Text = "";

                    StoredCredentialLoginService.RestoreResult restoreResult = null;
                    if (ShouldAttemptAutoLogin(account, operationName, allowInteractivePrompt))
                    {
                        restoreResult = await TryRestoreStoredSessionAsync(account, operationName, false);
                        if (restoreResult.Success)
                        {
                            return true;
                        }
                    }

                    if (allowInteractivePrompt)
                    {
                        bool hasStoredCredentials = storedCredentialLoginService.HasStoredCredentials(account.Session.SteamID, account.AccountName);
                        string message = (restoreResult?.CredentialsAvailable ?? hasStoredCredentials)
                            ? "Steam не смог обновить access token, и SDA++ тоже не смог автоматически восстановить сессию.\n\n" + (restoreResult?.Message ?? Localizer.Choose("Auto login failed.", "Автовход не удался.")) + "\n\nПожалуйста, войдите заново."
                            : "Steam не смог обновить access token для этого аккаунта.\n\nПожалуйста, войдите заново.";
                        MessageBox.Show(message, operationName, MessageBoxButtons.OK, MessageBoxIcon.Information);
                        PromptRefreshLogin(account);
                    }

                    return account.Session != null && !account.Session.IsRefreshTokenExpired();
                }
            }

            return true;
        }

        private async Task<StoredCredentialLoginService.RestoreResult> TryRestoreStoredSessionAsync(SteamGuardAccount account, string operationName, bool showOverlayStatus)
        {
            string originalStatus = lblStatus.Text;

            if (showOverlayStatus)
            {
        lblStatus.Text = Localizer.Choose("Restoring SDA++ session...", "Восстановление сессии SDA++...");
        ShowOverlay(operationName, Localizer.Choose("Restoring SDA++ session...", "Восстановление сессии SDA++..."));
            }

            StoredCredentialLoginService.RestoreResult result = await storedCredentialLoginService.TryRestoreSessionAsync(account);
            if (result.Success)
            {
                manifest.SaveAccount(account, manifest.Encrypted, passKey);
                loadAccountInfo();
                if (showOverlayStatus)
                {
            ShowOverlay(operationName, Localizer.Choose("SDA++ session restored.", "Сессия SDA++ восстановлена."));
                }
            }
            else if (showOverlayStatus && result.CredentialsAvailable)
            {
        ShowOverlay(operationName, Localizer.Choose("Auto-login failed.", "Автовход не удался."));
            }

            lblStatus.Text = originalStatus;
            return result;
        }

        private async Task MaybeAutoLoginAllAccountsOnStartupAsync()
        {
            if (startupBatchAutoLoginAttempted)
            {
                return;
            }

            startupBatchAutoLoginAttempted = true;
            await RunAutoLoginForAllAccountsAsync(true, false, Localizer.Choose("Auto login", "Автовход"));
        }

        private async Task RunAutoLoginForAllAccountsAsync(bool allowPrompt, bool initiatedByUser, string operationName)
        {
            if (manifest == null || !manifest.AutoLoginForExpiredSessions)
            {
                if (initiatedByUser)
                {
                    MessageBox.Show(
                        Localizer.Choose("Auto login for expired sessions is disabled in Settings.", "Автовход для истекших сессий отключен в настройках."),
                        operationName,
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);
                }

                return;
            }

            SteamGuardAccount[] sourceAccounts = allAccounts ?? Array.Empty<SteamGuardAccount>();
            List<SteamGuardAccount> targets = sourceAccounts.Where(NeedsBatchAutoLogin).ToList();
            if (targets.Count == 0)
            {
                if (initiatedByUser)
                {
                    MessageBox.Show(
                        Localizer.Choose("There are no accounts that need automatic login right now.", "Сейчас нет аккаунтов, которым нужен автоматический вход."),
                        operationName,
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);
                }

                return;
            }

            bool requiresCredentialRestore = targets.Any(account =>
                account?.Session != null
                && account.Session.IsRefreshTokenExpired()
                && storedCredentialLoginService.HasStoredCredentials(account.Session.SteamID, account.AccountName));

            if (allowPrompt && manifest.AskBeforeAutoLogin && requiresCredentialRestore)
            {
                DialogResult approval = MessageBox.Show(
                    Localizer.Choose(
                        $"Saved credentials were found for {targets.Count} account(s).\n\nSDA++ can try to restore all expired sessions automatically now.\n\nContinue?",
                        $"Найдены сохраненные логины для {targets.Count} аккаунтов.\n\nSDA++ может сейчас автоматически восстановить все истекшие сессии.\n\nПродолжить?"),
                    operationName,
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question);
                if (approval != DialogResult.Yes)
                {
                    return;
                }
            }

            string originalStatus = lblStatus.Text;
            string preferredAccountName = currentAccount?.AccountName;
            int recoveredCount = 0;
            int failedCount = 0;
            List<string> failedAccounts = new List<string>();

            try
            {
                foreach (SteamGuardAccount account in targets)
                {
                    lblStatus.Text = Localizer.Choose(
                        $"Restoring {account.AccountName}...",
                        $"Восстановление {account.AccountName}...");
                    bool ready = await EnsureAccountSessionReadyAsync(account, operationName, false);
                    if (ready)
                    {
                        recoveredCount++;
                    }
                    else
                    {
                        failedCount++;
                        failedAccounts.Add(account.AccountName);
                    }
                }
            }
            finally
            {
                lblStatus.Text = originalStatus;
                manifest = Manifest.GetManifest(true);
                loadAccountsList();
                ApplyAccountFilter(preferredAccountName);
                UpdateSelectedAccountCard();
            }

            if (initiatedByUser || recoveredCount > 0 || failedCount > 0)
            {
                string message = Localizer.Choose(
                    $"Recovered: {recoveredCount}\nFailed: {failedCount}",
                    $"Восстановлено: {recoveredCount}\nНе удалось: {failedCount}");

                if (failedAccounts.Count > 0)
                {
                    message += Localizer.Choose(
                        "\n\nAccounts that still need manual login:\n",
                        "\n\nАккаунты, которым все еще нужен ручной вход:\n")
                        + string.Join("\n", failedAccounts);
                }

                MessageBox.Show(
                    message,
                    operationName,
                    MessageBoxButtons.OK,
                    failedCount > 0 ? MessageBoxIcon.Warning : MessageBoxIcon.Information);
            }
        }

        private bool NeedsBatchAutoLogin(SteamGuardAccount account)
        {
            if (account?.Session == null)
            {
                return false;
            }

            if (account.Session.IsRefreshTokenExpired())
            {
                return storedCredentialLoginService.HasStoredCredentials(account.Session.SteamID, account.AccountName);
            }

            return account.Session.IsAccessTokenExpired();
        }

        /// <summary>
        /// Load UI with the current account info, this is run every second
        /// </summary>
        private void loadAccountInfo()
        {
            if (currentAccount != null && steamTime != 0)
            {
                popupFrm.Account = currentAccount;
                txtLoginToken.Text = currentAccount.GenerateSteamGuardCodeForTime(steamTime);
                int elapsed = (int)(steamTime - (currentSteamChunk * 30L));
                UpdateTimeoutBar(30 - elapsed, 30);
            }

            UpdateSelectedAccountCard();
        }

        /// <summary>
        /// Decrypts files and populates list UI with accounts
        /// </summary>
        private void loadAccountsList()
        {
            string preferredAccountName = currentAccount?.AccountName;
            currentAccount = null;

            listAccounts.Items.Clear();
            listAccounts.SelectedIndex = -1;

            trayAccountList.Items.Clear();
            trayAccountList.SelectedIndex = -1;

            allAccounts = manifest.GetAllAccounts(passKey);

            if (allAccounts.Length > 0)
            {
                ApplyAccountFilter(preferredAccountName);
            }
            else
            {
                UpdateSelectedAccountCard();
            }

            menuDeactivateAuthenticator.Enabled = btnTradeConfirmations.Enabled = menuTerminateSessions.Enabled = allAccounts.Length > 0;
        }

        private void listAccounts_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Control)
            {
                if (e.KeyCode == Keys.Up || e.KeyCode == Keys.Down)
                {
                    int to = listAccounts.SelectedIndex - (e.KeyCode == Keys.Up ? 1 : -1);
                    manifest.MoveEntry(listAccounts.SelectedIndex, to);
                    loadAccountsList();
                }
                return;
            }

            if (!IsKeyAChar(e.KeyCode) && !IsKeyADigit(e.KeyCode))
            {
                return;
            }

            txtAccSearch.Focus();
            char typedChar = GetSearchChar(e);
            if (typedChar == '\0')
            {
                return;
            }

            txtAccSearch.Text = typedChar.ToString();
            txtAccSearch.SelectionStart = txtAccSearch.Text.Length;
        }

        private static bool IsKeyAChar(Keys key)
        {
            return key >= Keys.A && key <= Keys.Z;
        }

        private static bool IsKeyADigit(Keys key)
        {
            return (key >= Keys.D0 && key <= Keys.D9) || (key >= Keys.NumPad0 && key <= Keys.NumPad9);
        }

        private void SelectRelativeAccount(int offset)
        {
            if (listAccounts.Items.Count == 0)
            {
                ShowOverlay(Localizer.Choose("Accounts", "Аккаунты"), Localizer.Choose("Accounts are not loaded.", "Аккаунты не загружены."));
                return;
            }

            int currentIndex = listAccounts.SelectedIndex;
            if (currentIndex < 0)
            {
                currentIndex = 0;
            }
            else
            {
                currentIndex = (currentIndex + offset + listAccounts.Items.Count) % listAccounts.Items.Count;
            }

            listAccounts.SelectedIndex = currentIndex;
            ShowOverlay(Localizer.Choose("Active account", "Активный аккаунт"), filteredAccounts[currentIndex].AccountName);
        }

        private void ApplyAccountFilter(string preferredAccountName = null)
        {
            filteredAccounts = allAccounts
                .Where(account => IsFilter(account.AccountName))
                .OrderByDescending(account => manifest.IsFavorite(account.Session.SteamID))
                .ThenBy(account => account.AccountName, StringComparer.OrdinalIgnoreCase)
                .ToList();

            listAccounts.Items.Clear();
            trayAccountList.Items.Clear();

            foreach (SteamGuardAccount account in filteredAccounts)
            {
                listAccounts.Items.Add(GetDisplayName(account));
                trayAccountList.Items.Add(account.AccountName);
            }

            string targetName = preferredAccountName ?? currentAccount?.AccountName;
            int selectedIndex = filteredAccounts.FindIndex(account => StringComparer.OrdinalIgnoreCase.Equals(account.AccountName, targetName));
            if (selectedIndex < 0 && filteredAccounts.Count > 0)
            {
                selectedIndex = 0;
            }

            if (selectedIndex >= 0)
            {
                listAccounts.SelectedIndex = selectedIndex;
                trayAccountList.SelectedIndex = selectedIndex;
            }
            else
            {
                currentAccount = null;
                trayAccountList.Text = "";
                txtLoginToken.Text = "";
                UpdateSelectedAccountCard();
            }

            bool hasSelectedAccount = listAccounts.SelectedIndex >= 0;
            btnTradeConfirmations.Enabled = menuDeactivateAuthenticator.Enabled = menuTerminateSessions.Enabled = hasSelectedAccount;
            btnQuickLoginAgain.Enabled = btnQuickTerminate.Enabled = btnFavoriteAccount.Enabled = hasSelectedAccount;
            UpdateSelectedAccountCard();
        }

        private static char GetSearchChar(KeyEventArgs e)
        {
            if (e.KeyCode >= Keys.A && e.KeyCode <= Keys.Z)
            {
                return (char)('a' + (e.KeyCode - Keys.A));
            }

            if (e.KeyCode >= Keys.D0 && e.KeyCode <= Keys.D9)
            {
                return (char)('0' + (e.KeyCode - Keys.D0));
            }

            if (e.KeyCode >= Keys.NumPad0 && e.KeyCode <= Keys.NumPad9)
            {
                return (char)('0' + (e.KeyCode - Keys.NumPad0));
            }

            return '\0';
        }

        private bool IsFilter(string accountName)
        {
            string filter = txtAccSearch.Text?.Trim() ?? "";
            if (filter.Length == 0)
            {
                return true;
            }

            if (filter.StartsWith("~"))
            {
                try
                {
                    return Regex.IsMatch(accountName, filter.Substring(1), RegexOptions.IgnoreCase);
                }
                catch (Exception)
                {
                    return true;
                }
            }

            string[] parts = filter.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            return parts.All(part => accountName.IndexOf(part, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private string GetDisplayName(SteamGuardAccount account)
        {
            return manifest != null && manifest.IsFavorite(account.Session.SteamID)
                ? "★ " + account.AccountName
                : account.AccountName;
        }

        private void ToggleCurrentAccountFavorite()
        {
            if (currentAccount == null)
            {
                return;
            }

            bool newFavoriteState = !manifest.IsFavorite(currentAccount.Session.SteamID);
            manifest.SetFavorite(currentAccount.Session.SteamID, newFavoriteState);
            ApplyAccountFilter(currentAccount.AccountName);
            ShowOverlay(Localizer.Choose("Favorites", "Избранное"), newFavoriteState
                ? Localizer.Choose(currentAccount.AccountName + " pinned.", currentAccount.AccountName + " закреплен.")
                : Localizer.Choose(currentAccount.AccountName + " unpinned.", currentAccount.AccountName + " откреплен."));
        }

        private void UpdateSelectedAccountCard()
        {
            if (currentAccount == null)
            {
                lblAccountName.Text = Localizer.Choose("No account selected", "Аккаунт не выбран");
                lblSessionBadge.Text = Localizer.Choose("Idle", "Ожидание");
                lblSessionBadge.BackColor = Branding.AccentSoft;
                lblSessionBadge.ForeColor = Branding.AccentDark;
                lblSessionDetails.Text = Localizer.Choose("Steam Guard codes stay ready here once an account is selected.", "Коды Steam Guard будут отображаться здесь после выбора аккаунта.");
                btnFavoriteAccount.Text = Localizer.Choose("Pin", "Закрепить");
                btnQuickLoginAgain.Enabled = false;
                btnQuickTerminate.Enabled = false;
                btnFavoriteAccount.Enabled = false;
                btnTradeConfirmations.Enabled = false;
                return;
            }

            lblAccountName.Text = currentAccount.AccountName;
            btnFavoriteAccount.Text = manifest.IsFavorite(currentAccount.Session.SteamID) ? "Pinned" : "Pin";
            bool hasStoredCredentials = storedCredentialLoginService.HasStoredCredentials(currentAccount.Session?.SteamID ?? 0, currentAccount.AccountName);
            bool isExpired = currentAccount.Session == null || currentAccount.Session.IsAccessTokenExpired() || currentAccount.Session.IsRefreshTokenExpired();
            btnQuickLoginAgain.Enabled = true;
            btnQuickTerminate.Enabled = true;
            btnFavoriteAccount.Enabled = true;
            btnTradeConfirmations.Enabled = true;
            btnQuickLoginAgain.Text = isExpired && hasStoredCredentials && manifest.AutoLoginForExpiredSessions
                ? Localizer.Choose("Auto-login", "Автовход")
                : Localizer.Choose("Login again", "Войти заново");
            menuLoginAgain.Text = isExpired && hasStoredCredentials && manifest.AutoLoginForExpiredSessions
                ? Localizer.Choose("Login again automatically", "Войти автоматически")
                : Localizer.Choose("Login again", "Войти заново");

            SessionStatus status = GetSessionStatus(currentAccount);
            lblSessionBadge.Visible = status.ShowBadge;
            lblSessionBadge.Text = status.Title;
            lblSessionBadge.BackColor = status.BackColor;
            lblSessionBadge.ForeColor = status.ForeColor;
            lblSessionDetails.Text = status.Description;
        }

        private SessionStatus GetSessionStatus(SteamGuardAccount account)
        {
            if (account?.Session == null)
            {
                return new SessionStatus(Localizer.Choose("No session", "Нет сессии"), Localizer.Choose("Sign in again to restore Steam web actions.", "Войдите заново, чтобы восстановить веб-действия Steam."), Branding.Danger, Color.White, true);
            }

            if (account.Session.IsRefreshTokenExpired())
            {
                bool hasStoredCredentials = storedCredentialLoginService.HasStoredCredentials(account.Session.SteamID, account.AccountName);
                string description = hasStoredCredentials
                    ? Localizer.Choose("Credentials found. SDA++ can log in again automatically for this account.", "Логины найдены. SDA++ может автоматически войти для этого аккаунта.")
                    : Localizer.Choose("Credentials missing. This account needs Login again before QR approvals and confirmations can work.", "Логины не найдены. Этому аккаунту нужен повторный вход, прежде чем заработают QR-подтверждения и подтверждения обменов.");
                return new SessionStatus("", description, Branding.Danger, Color.White, false);
            }

            if (account.Session.IsAccessTokenExpired())
            {
                bool hasStoredCredentials = storedCredentialLoginService.HasStoredCredentials(account.Session.SteamID, account.AccountName);
                return new SessionStatus(
                    Localizer.Choose("Access expired", "Доступ истек"),
                    hasStoredCredentials
                        ? Localizer.Choose("Credentials found. SDA++ can refresh this account automatically if Steam refuses the access token.", "Логины найдены. SDA++ сможет автоматически восстановить аккаунт, если Steam отклонит access token.")
                        : Localizer.Choose("Steam Guard codes still rotate, but web actions may ask you to log in again.", "Коды Steam Guard продолжают обновляться, но веб-действия могут попросить повторный вход."),
                    Branding.Warning,
                    Branding.HeadingText,
                    true);
            }

            return new SessionStatus(Localizer.Choose("Session live", "Сессия активна"), Localizer.Choose("Ready for QR approvals, confirmations, and background session tasks.", "Готово к QR-подтверждениям, подтверждениям и фоновым задачам сессии."), Branding.Success, Color.White, true);
        }

        private void UpdateTimeoutBar(int value, int maxValue)
        {
            timeoutBarMax = maxValue <= 0 ? 1 : maxValue;
            timeoutBarValue = Math.Max(0, Math.Min(value, timeoutBarMax));
            panelTimeoutTrack.Invalidate();
        }

        private void panelTimeoutTrack_Paint(object sender, PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            Rectangle trackRect = new Rectangle(0, 0, Math.Max(0, panelTimeoutTrack.Width - 1), Math.Max(0, panelTimeoutTrack.Height - 1));
            using (var trackPath = CreateRoundedPath(trackRect, 7))
            using (var trackBrush = new SolidBrush(Color.FromArgb(190, Branding.AccentDark)))
            using (var borderPen = new Pen(Color.FromArgb(150, Branding.Outline)))
            {
                e.Graphics.FillPath(trackBrush, trackPath);
                e.Graphics.DrawPath(borderPen, trackPath);
            }

            Rectangle fillRect = GetTimeoutFillRectangle();
            if (fillRect.Width <= 0 || fillRect.Height <= 0)
            {
                return;
            }

            int fillRadius = Math.Min(7, Math.Max(1, fillRect.Height / 2));
            using (var fillPath = CreateRoundedPath(fillRect, fillRadius))
            using (var fillBrush = new SolidBrush(Branding.Accent))
            {
                e.Graphics.FillPath(fillBrush, fillPath);
            }
        }

        private void panelTimeoutTrack_Resize(object sender, EventArgs e)
        {
            using (var path = CreateRoundedPath(new Rectangle(0, 0, Math.Max(0, panelTimeoutTrack.Width - 1), Math.Max(0, panelTimeoutTrack.Height - 1)), 7))
            {
                panelTimeoutTrack.Region = new Region(path);
            }

            panelTimeoutTrack.Invalidate();
        }

        private Rectangle GetTimeoutFillRectangle()
        {
            if (timeoutBarValue <= 0 || timeoutBarMax <= 0)
            {
                return Rectangle.Empty;
            }

            const int inset = 1;
            int width = Math.Max(0, panelTimeoutTrack.Width - (inset * 2));
            int height = Math.Max(0, panelTimeoutTrack.Height - (inset * 2));
            int fillWidth = width * timeoutBarValue / timeoutBarMax;

            return new Rectangle(inset, inset, fillWidth, height);
        }

        private static System.Drawing.Drawing2D.GraphicsPath CreateRoundedPath(Rectangle rect, int radius)
        {
            int safeRadius = Math.Max(1, radius);
            int diameter = safeRadius * 2;
            var path = new System.Drawing.Drawing2D.GraphicsPath();
            path.AddArc(rect.X, rect.Y, diameter, diameter, 180, 90);
            path.AddArc(rect.Right - diameter, rect.Y, diameter, diameter, 270, 90);
            path.AddArc(rect.Right - diameter, rect.Bottom - diameter, diameter, diameter, 0, 90);
            path.AddArc(rect.X, rect.Bottom - diameter, diameter, diameter, 90, 90);
            path.CloseFigure();
            return path;
        }

        private void listAccounts_DrawItem(object sender, DrawItemEventArgs e)
        {
            e.DrawBackground();
            if (e.Index < 0 || e.Index >= filteredAccounts.Count)
            {
                return;
            }

            SteamGuardAccount account = filteredAccounts[e.Index];
            bool selected = (e.State & DrawItemState.Selected) == DrawItemState.Selected;
            bool accessExpired = account.Session != null && account.Session.IsAccessTokenExpired() && !account.Session.IsRefreshTokenExpired();
            Color background = selected ? Branding.Accent : accessExpired ? Color.FromArgb(84, 42, 48) : Branding.AccentDark;
            Color foreground = selected ? Color.White : Branding.HeadingText;
            using (SolidBrush backgroundBrush = new SolidBrush(background))
            using (SolidBrush foregroundBrush = new SolidBrush(foreground))
            using (SolidBrush starBrush = new SolidBrush(selected ? Color.FromArgb(255, 241, 166) : Color.FromArgb(234, 186, 84)))
            {
                e.Graphics.FillRectangle(backgroundBrush, e.Bounds);

                int x = e.Bounds.X + 8;
                if (manifest.IsFavorite(account.Session.SteamID))
                {
                    e.Graphics.DrawString("★", new Font("Segoe UI Symbol", 9F, FontStyle.Regular, GraphicsUnit.Point), starBrush, x, e.Bounds.Y + 1);
                    x += 16;
                }

                e.Graphics.DrawString(account.AccountName, e.Font, foregroundBrush, x, e.Bounds.Y + 2);

                if (account.Session != null && account.Session.IsRefreshTokenExpired())
                {
                    using (Pen expiredPen = new Pen(Branding.Danger, 1))
                    {
                        Rectangle borderBounds = new Rectangle(e.Bounds.X, e.Bounds.Y, e.Bounds.Width - 1, e.Bounds.Height - 1);
                        e.Graphics.DrawRectangle(expiredPen, borderBounds);
                    }
                }
            }

            e.DrawFocusRectangle();
        }

        private readonly struct SessionStatus
        {
            public SessionStatus(string title, string description, Color backColor, Color foreColor, bool showBadge)
            {
                Title = title;
                Description = description;
                BackColor = backColor;
                ForeColor = foreColor;
                ShowBadge = showBadge;
            }

            public string Title { get; }
            public string Description { get; }
            public Color BackColor { get; }
            public Color ForeColor { get; }
            public bool ShowBadge { get; }
        }

        private sealed class GraphiteMenuColors : ProfessionalColorTable
        {
            public override Color MenuItemSelected => Branding.AccentSoft;
            public override Color MenuItemSelectedGradientBegin => Branding.AccentSoft;
            public override Color MenuItemSelectedGradientEnd => Branding.AccentSoft;
            public override Color MenuItemBorder => Branding.Outline;
            public override Color MenuBorder => Branding.Outline;
            public override Color ToolStripBorder => Branding.Outline;
            public override Color ToolStripDropDownBackground => Branding.CardBackground;
            public override Color ImageMarginGradientBegin => Branding.CardBackground;
            public override Color ImageMarginGradientMiddle => Branding.CardBackground;
            public override Color ImageMarginGradientEnd => Branding.CardBackground;
            public override Color MenuStripGradientBegin => Branding.AccentDark;
            public override Color MenuStripGradientEnd => Branding.AccentDark;
        }

        private sealed class GraphiteMenuRenderer : ToolStripProfessionalRenderer
        {
            public GraphiteMenuRenderer() : base(new GraphiteMenuColors())
            {
            }

            protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
            {
                Rectangle bounds = new Rectangle(Point.Empty, e.Item.Size);
                Color fillColor = e.Item.Selected
                    ? Branding.AccentSoft
                    : (e.ToolStrip is MenuStrip ? Branding.AccentDark : Branding.CardBackground);

                using (SolidBrush brush = new SolidBrush(fillColor))
                {
                    e.Graphics.FillRectangle(brush, bounds);
                }

                if (e.Item.Selected)
                {
                    using (Pen borderPen = new Pen(Branding.Outline))
                    {
                        e.Graphics.DrawRectangle(borderPen, 0, 0, bounds.Width - 1, bounds.Height - 1);
                    }
                }
            }

            protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
            {
                e.TextColor = Branding.HeadingText;
                base.OnRenderItemText(e);
            }

            protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
            {
                Rectangle bounds = new Rectangle(Point.Empty, e.Item.Size);
                using (Pen pen = new Pen(Branding.Outline))
                {
                    int y = bounds.Height / 2;
                    e.Graphics.DrawLine(pen, 8, y, bounds.Width - 8, y);
                }
            }

            protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
            {
                e.ArrowColor = Branding.MutedText;
                base.OnRenderArrow(e);
            }
        }

        private void loadSettings()
        {
            timerTradesPopup.Enabled = manifest.PeriodicChecking;
            timerTradesPopup.Interval = manifest.PeriodicCheckingInterval * 1000;
            SetQrHotkeysEnabled(manifest.QrHotkeysEnabled, false, false);
            hotkeyOverlay.SetAnchorMode(manifest.QrCaptureMode == QrCaptureMode.AreaAroundCursor
                ? HotkeyOverlayAnchor.TopRight
                : HotkeyOverlayAnchor.TopCenter);
        }

        private void RegisterGlobalHotkeys()
        {
            if (!IsHandleCreated)
            {
                return;
            }

            UnregisterGlobalHotkeys();
            RegisterHotKeyBinding(HOTKEY_TOGGLE_QR, GetHotkeyBinding(HOTKEY_TOGGLE_QR));
            RegisterHotKeyBinding(HOTKEY_SCAN_QR, GetHotkeyBinding(HOTKEY_SCAN_QR));
            RegisterHotKeyBinding(HOTKEY_PREVIOUS_ACCOUNT, GetHotkeyBinding(HOTKEY_PREVIOUS_ACCOUNT));
            RegisterHotKeyBinding(HOTKEY_NEXT_ACCOUNT, GetHotkeyBinding(HOTKEY_NEXT_ACCOUNT));
        }

        private void UnregisterGlobalHotkeys()
        {
            UnregisterHotKey(this.Handle, HOTKEY_TOGGLE_QR);
            UnregisterHotKey(this.Handle, HOTKEY_SCAN_QR);
            UnregisterHotKey(this.Handle, HOTKEY_PREVIOUS_ACCOUNT);
            UnregisterHotKey(this.Handle, HOTKEY_NEXT_ACCOUNT);
        }

        private void SetQrHotkeysEnabled(bool enabled, bool showOverlay, bool persist)
        {
            qrHotkeysEnabled = enabled;

            if (chkEnableQrHotkeys.Checked != enabled)
            {
                suppressQrToggleEvents = true;
                chkEnableQrHotkeys.Checked = enabled;
                suppressQrToggleEvents = false;
            }

            if (persist && manifest != null && manifest.QrHotkeysEnabled != enabled)
            {
                manifest.QrHotkeysEnabled = enabled;
                manifest.Save();
            }

            if (showOverlay)
            {
                ShowOverlay(Localizer.Choose("Steam QR hotkeys", "QR-хоткеи Steam"), enabled
                    ? Localizer.Choose("Enabled", "Включены")
                    : Localizer.Choose("Disabled", "Выключены"));
            }
        }

        private void RegisterHotKeyBinding(int id, HotkeyBinding binding)
        {
            HotkeyBinding normalized = binding ?? GetHotkeyBinding(id);
            uint modifiers = MOD_NOREPEAT;
            if (normalized.Control)
            {
                modifiers |= MOD_CONTROL;
            }

            if (normalized.Shift)
            {
                modifiers |= MOD_SHIFT;
            }

            if (normalized.Alt)
            {
                modifiers |= MOD_ALT;
            }

            RegisterHotKey(this.Handle, id, modifiers, (uint)normalized.KeyCode);
        }

        private HotkeyBinding GetHotkeyBinding(int hotkeyId)
        {
            if (manifest == null)
            {
                switch (hotkeyId)
                {
                    case HOTKEY_TOGGLE_QR:
                        return HotkeyBindingHelper.CreateDefault(Keys.Q);
                    case HOTKEY_SCAN_QR:
                        return HotkeyBindingHelper.CreateDefault(Keys.S);
                    case HOTKEY_PREVIOUS_ACCOUNT:
                        return HotkeyBindingHelper.CreateDefault(Keys.Left);
                    default:
                        return HotkeyBindingHelper.CreateDefault(Keys.Right);
                }
            }

            switch (hotkeyId)
            {
                case HOTKEY_TOGGLE_QR:
                    return HotkeyBindingHelper.Normalize(manifest.QrHotkeyToggle, Keys.Q);
                case HOTKEY_SCAN_QR:
                    return HotkeyBindingHelper.Normalize(manifest.QrHotkeyScan, Keys.S);
                case HOTKEY_PREVIOUS_ACCOUNT:
                    return HotkeyBindingHelper.Normalize(manifest.AccountHotkeyPrevious, Keys.Left);
                default:
                    return HotkeyBindingHelper.Normalize(manifest.AccountHotkeyNext, Keys.Right);
            }
        }

        private void ShowOverlay(string title, string message)
        {
            if (this.IsDisposed)
            {
                return;
            }

            if (InvokeRequired)
            {
                BeginInvoke(new Action<string, string>(ShowOverlay), title, message);
                return;
            }

            hotkeyOverlay.ShowStatus(title, message);
        }

        private static string GetFullExceptionMessage(Exception ex)
        {
            if (ex == null)
            {
                return "Unknown error.";
            }

            List<string> messages = new List<string>();
            Exception current = ex;
            while (current != null)
            {
                if (!string.IsNullOrWhiteSpace(current.Message))
                {
                    messages.Add(current.Message.Trim());
                }

                current = current.InnerException;
            }

            return string.Join("\n\n", messages.Distinct());
        }

        private static string GetInnermostExceptionMessage(Exception ex)
        {
            if (ex == null)
            {
                return "Unknown error.";
            }

            while (ex.InnerException != null)
            {
                ex = ex.InnerException;
            }

            return string.IsNullOrWhiteSpace(ex.Message) ? "Unknown error." : ex.Message;
        }

        private void checkForUpdates()
        {
            labelUpdate.Text = "GitHub";
            lblFooterKofi.Text = "Ko-fi";
        }

        private void MainForm_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.C && e.Modifiers == Keys.Control)
            {
                CopyLoginToken();
            }
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_HOTKEY)
            {
                int hotkeyId = m.WParam.ToInt32();
                BeginInvoke(new Action(async () => await HandleGlobalHotkeyAsync(hotkeyId)));
            }

            base.WndProc(ref m);
        }

        private async Task HandleGlobalHotkeyAsync(int hotkeyId)
        {
            switch (hotkeyId)
            {
                case HOTKEY_TOGGLE_QR:
                    SetQrHotkeysEnabled(!qrHotkeysEnabled, true, true);
                    break;
                case HOTKEY_SCAN_QR:
                    if (!qrHotkeysEnabled)
                    {
                    ShowOverlay(
                        Localizer.Choose("Steam QR hotkeys", "QR-хоткеи Steam"),
                        Localizer.Choose("Enable them first with ", "Сначала включите их через ")
                        + HotkeyBindingHelper.ToDisplayText(GetHotkeyBinding(HOTKEY_TOGGLE_QR))
                        + ".");
                        return;
                    }

                    await RunQrScanAsync(false);
                    break;
                case HOTKEY_PREVIOUS_ACCOUNT:
                    SelectRelativeAccount(-1);
                    break;
                case HOTKEY_NEXT_ACCOUNT:
                    SelectRelativeAccount(1);
                    break;
            }
        }

        private void panelButtons_SizeChanged(object sender, EventArgs e)
        {
            LayoutActionButtons();
        }
    }
}
