namespace Steam_Desktop_Authenticator
{
    partial class SettingsForm
    {
        private System.ComponentModel.IContainer components = null;

        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        private void InitializeComponent()
        {
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(SettingsForm));
            this.chkPeriodicChecking = new System.Windows.Forms.CheckBox();
            this.btnSave = new System.Windows.Forms.Button();
            this.numPeriodicInterval = new System.Windows.Forms.NumericUpDown();
            this.label1 = new System.Windows.Forms.Label();
            this.labelLanguage = new System.Windows.Forms.Label();
            this.cmbLanguage = new System.Windows.Forms.ComboBox();
            this.chkCheckAll = new System.Windows.Forms.CheckBox();
            this.chkConfirmMarket = new System.Windows.Forms.CheckBox();
            this.chkConfirmTrades = new System.Windows.Forms.CheckBox();
            this.groupQr = new System.Windows.Forms.GroupBox();
            this.btnResetHotkeys = new System.Windows.Forms.Button();
            this.lblCursorScanSize = new System.Windows.Forms.Label();
            this.numCursorScanSize = new System.Windows.Forms.NumericUpDown();
            this.cmbQrCaptureMode = new System.Windows.Forms.ComboBox();
            this.label7 = new System.Windows.Forms.Label();
            this.txtHotkeyNext = new System.Windows.Forms.TextBox();
            this.label6 = new System.Windows.Forms.Label();
            this.txtHotkeyPrev = new System.Windows.Forms.TextBox();
            this.label5 = new System.Windows.Forms.Label();
            this.txtHotkeyScan = new System.Windows.Forms.TextBox();
            this.label4 = new System.Windows.Forms.Label();
            this.txtHotkeyToggle = new System.Windows.Forms.TextBox();
            this.label3 = new System.Windows.Forms.Label();
            this.chkEnableQrHotkeys = new System.Windows.Forms.CheckBox();
            this.groupCloud = new System.Windows.Forms.GroupBox();
            this.lblCloudLastSyncValue = new System.Windows.Forms.Label();
            this.lblCloudLastSyncTitle = new System.Windows.Forms.Label();
            this.btnCloudOpenBackups = new System.Windows.Forms.Button();
            this.lblCloudStatus = new System.Windows.Forms.Label();
            this.btnCloudPush = new System.Windows.Forms.Button();
            this.btnCloudPull = new System.Windows.Forms.Button();
            this.btnCloudTest = new System.Windows.Forms.Button();
            this.chkSyncStoredCredentials = new System.Windows.Forms.CheckBox();
            this.txtWebDavPath = new System.Windows.Forms.TextBox();
            this.label11 = new System.Windows.Forms.Label();
            this.txtWebDavPassword = new System.Windows.Forms.TextBox();
            this.label10 = new System.Windows.Forms.Label();
            this.txtWebDavUsername = new System.Windows.Forms.TextBox();
            this.label9 = new System.Windows.Forms.Label();
            this.txtWebDavUrl = new System.Windows.Forms.TextBox();
            this.label8 = new System.Windows.Forms.Label();
            ((System.ComponentModel.ISupportInitialize)(this.numPeriodicInterval)).BeginInit();
            this.groupQr.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.numCursorScanSize)).BeginInit();
            this.groupCloud.SuspendLayout();
            this.SuspendLayout();
            // 
            // chkPeriodicChecking
            // 
            this.chkPeriodicChecking.AutoSize = true;
            this.chkPeriodicChecking.Location = new System.Drawing.Point(12, 44);
            this.chkPeriodicChecking.Name = "chkPeriodicChecking";
            this.chkPeriodicChecking.Size = new System.Drawing.Size(233, 30);
            this.chkPeriodicChecking.TabIndex = 0;
            this.chkPeriodicChecking.Text = "Периодически проверять новые подтверждения\r\nи показывать всплывающее окно";
            this.chkPeriodicChecking.UseVisualStyleBackColor = true;
            this.chkPeriodicChecking.CheckedChanged += new System.EventHandler(this.chkPeriodicChecking_CheckedChanged);
            // 
            // btnSave
            // 
            this.btnSave.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.btnSave.Font = new System.Drawing.Font("Segoe UI", 12F);
            this.btnSave.Location = new System.Drawing.Point(12, 932);
            this.btnSave.Name = "btnSave";
            this.btnSave.Size = new System.Drawing.Size(360, 40);
            this.btnSave.TabIndex = 25;
            this.btnSave.Text = "Сохранить";
            this.btnSave.UseVisualStyleBackColor = true;
            this.btnSave.Click += new System.EventHandler(this.btnSave_Click);
            // 
            // numPeriodicInterval
            // 
            this.numPeriodicInterval.Location = new System.Drawing.Point(12, 83);
            this.numPeriodicInterval.Minimum = new decimal(new int[] {
            5,
            0,
            0,
            0});
            this.numPeriodicInterval.Name = "numPeriodicInterval";
            this.numPeriodicInterval.Size = new System.Drawing.Size(52, 22);
            this.numPeriodicInterval.TabIndex = 1;
            this.numPeriodicInterval.Value = new decimal(new int[] {
            5,
            0,
            0,
            0});
            // 
            // label1
            // 
            this.label1.AutoSize = true;
            this.label1.Location = new System.Drawing.Point(70, 81);
            this.label1.Name = "label1";
            this.label1.Size = new System.Drawing.Size(150, 26);
            this.label1.TabIndex = 3;
            this.label1.Text = "Секунд между проверками\r\nподтверждений";
            // 
            // labelLanguage
            // 
            this.labelLanguage.AutoSize = true;
            this.labelLanguage.Location = new System.Drawing.Point(12, 12);
            this.labelLanguage.Name = "labelLanguage";
            this.labelLanguage.Size = new System.Drawing.Size(43, 13);
            this.labelLanguage.TabIndex = 15;
            this.labelLanguage.Text = "Язык:";
            // 
            // cmbLanguage
            // 
            this.cmbLanguage.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.cmbLanguage.FormattingEnabled = true;
            this.cmbLanguage.Location = new System.Drawing.Point(73, 9);
            this.cmbLanguage.Name = "cmbLanguage";
            this.cmbLanguage.Size = new System.Drawing.Size(145, 21);
            this.cmbLanguage.TabIndex = 16;
            // 
            // chkCheckAll
            // 
            this.chkCheckAll.AutoSize = true;
            this.chkCheckAll.Location = new System.Drawing.Point(12, 113);
            this.chkCheckAll.Name = "chkCheckAll";
            this.chkCheckAll.Size = new System.Drawing.Size(213, 17);
            this.chkCheckAll.TabIndex = 2;
            this.chkCheckAll.Text = "Проверять подтверждения у всех аккаунтов";
            this.chkCheckAll.UseVisualStyleBackColor = true;
            // 
            // chkConfirmMarket
            // 
            this.chkConfirmMarket.AutoSize = true;
            this.chkConfirmMarket.Location = new System.Drawing.Point(12, 136);
            this.chkConfirmMarket.Name = "chkConfirmMarket";
            this.chkConfirmMarket.Size = new System.Drawing.Size(198, 17);
            this.chkConfirmMarket.TabIndex = 3;
            this.chkConfirmMarket.Text = "Автоподтверждение продаж на маркете";
            this.chkConfirmMarket.UseVisualStyleBackColor = true;
            this.chkConfirmMarket.CheckedChanged += new System.EventHandler(this.chkConfirmMarket_CheckedChanged);
            // 
            // chkConfirmTrades
            // 
            this.chkConfirmTrades.AutoSize = true;
            this.chkConfirmTrades.Location = new System.Drawing.Point(12, 159);
            this.chkConfirmTrades.Name = "chkConfirmTrades";
            this.chkConfirmTrades.Size = new System.Drawing.Size(129, 17);
            this.chkConfirmTrades.TabIndex = 4;
            this.chkConfirmTrades.Text = "Автоподтверждение обменов";
            this.chkConfirmTrades.UseVisualStyleBackColor = true;
            this.chkConfirmTrades.CheckedChanged += new System.EventHandler(this.chkConfirmTrades_CheckedChanged);
            // 
            // groupQr
            // 
            this.groupQr.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.groupQr.Controls.Add(this.btnResetHotkeys);
            this.groupQr.Controls.Add(this.lblCursorScanSize);
            this.groupQr.Controls.Add(this.numCursorScanSize);
            this.groupQr.Controls.Add(this.cmbQrCaptureMode);
            this.groupQr.Controls.Add(this.label7);
            this.groupQr.Controls.Add(this.txtHotkeyNext);
            this.groupQr.Controls.Add(this.label6);
            this.groupQr.Controls.Add(this.txtHotkeyPrev);
            this.groupQr.Controls.Add(this.label5);
            this.groupQr.Controls.Add(this.txtHotkeyScan);
            this.groupQr.Controls.Add(this.label4);
            this.groupQr.Controls.Add(this.txtHotkeyToggle);
            this.groupQr.Controls.Add(this.label3);
            this.groupQr.Controls.Add(this.chkEnableQrHotkeys);
            this.groupQr.Location = new System.Drawing.Point(12, 196);
            this.groupQr.Name = "groupQr";
            this.groupQr.Size = new System.Drawing.Size(360, 318);
            this.groupQr.TabIndex = 5;
            this.groupQr.TabStop = false;
            this.groupQr.Text = "QR-вход и хоткеи";
            // 
            // btnResetHotkeys
            // 
            this.btnResetHotkeys.Location = new System.Drawing.Point(232, 270);
            this.btnResetHotkeys.Name = "btnResetHotkeys";
            this.btnResetHotkeys.Size = new System.Drawing.Size(116, 28);
            this.btnResetHotkeys.TabIndex = 13;
            this.btnResetHotkeys.Text = "Сбросить";
            this.btnResetHotkeys.UseVisualStyleBackColor = true;
            this.btnResetHotkeys.Click += new System.EventHandler(this.btnResetHotkeys_Click);
            // 
            // lblCursorScanSize
            // 
            this.lblCursorScanSize.AutoSize = true;
            this.lblCursorScanSize.Location = new System.Drawing.Point(9, 274);
            this.lblCursorScanSize.Name = "lblCursorScanSize";
            this.lblCursorScanSize.Size = new System.Drawing.Size(134, 13);
            this.lblCursorScanSize.TabIndex = 12;
            this.lblCursorScanSize.Text = "Размер области у курсора (пикс.):";
            // 
            // numCursorScanSize
            // 
            this.numCursorScanSize.Increment = new decimal(new int[] {
            50,
            0,
            0,
            0});
            this.numCursorScanSize.Location = new System.Drawing.Point(158, 271);
            this.numCursorScanSize.Maximum = new decimal(new int[] {
            1600,
            0,
            0,
            0});
            this.numCursorScanSize.Minimum = new decimal(new int[] {
            250,
            0,
            0,
            0});
            this.numCursorScanSize.Name = "numCursorScanSize";
            this.numCursorScanSize.Size = new System.Drawing.Size(70, 22);
            this.numCursorScanSize.TabIndex = 12;
            this.numCursorScanSize.Value = new decimal(new int[] {
            700,
            0,
            0,
            0});
            // 
            // cmbQrCaptureMode
            // 
            this.cmbQrCaptureMode.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.cmbQrCaptureMode.FormattingEnabled = true;
            this.cmbQrCaptureMode.Location = new System.Drawing.Point(9, 234);
            this.cmbQrCaptureMode.Name = "cmbQrCaptureMode";
            this.cmbQrCaptureMode.Size = new System.Drawing.Size(339, 21);
            this.cmbQrCaptureMode.TabIndex = 11;
            this.cmbQrCaptureMode.SelectedIndexChanged += new System.EventHandler(this.cmbQrCaptureMode_SelectedIndexChanged);
            // 
            // label7
            // 
            this.label7.AutoSize = true;
            this.label7.Location = new System.Drawing.Point(9, 214);
            this.label7.Name = "label7";
            this.label7.Size = new System.Drawing.Size(104, 13);
            this.label7.TabIndex = 9;
            this.label7.Text = "Источник захвата QR:";
            // 
            // txtHotkeyNext
            // 
            this.txtHotkeyNext.Location = new System.Drawing.Point(9, 180);
            this.txtHotkeyNext.Name = "txtHotkeyNext";
            this.txtHotkeyNext.ReadOnly = true;
            this.txtHotkeyNext.Size = new System.Drawing.Size(339, 22);
            this.txtHotkeyNext.TabIndex = 10;
            this.txtHotkeyNext.KeyDown += new System.Windows.Forms.KeyEventHandler(this.txtHotkey_KeyDown);
            // 
            // label6
            // 
            this.label6.AutoSize = true;
            this.label6.Location = new System.Drawing.Point(9, 160);
            this.label6.Name = "label6";
            this.label6.Size = new System.Drawing.Size(121, 13);
            this.label6.TabIndex = 7;
            this.label6.Text = "Переключить на следующий аккаунт:";
            // 
            // txtHotkeyPrev
            // 
            this.txtHotkeyPrev.Location = new System.Drawing.Point(9, 126);
            this.txtHotkeyPrev.Name = "txtHotkeyPrev";
            this.txtHotkeyPrev.ReadOnly = true;
            this.txtHotkeyPrev.Size = new System.Drawing.Size(339, 22);
            this.txtHotkeyPrev.TabIndex = 8;
            this.txtHotkeyPrev.KeyDown += new System.Windows.Forms.KeyEventHandler(this.txtHotkey_KeyDown);
            // 
            // label5
            // 
            this.label5.AutoSize = true;
            this.label5.Location = new System.Drawing.Point(9, 106);
            this.label5.Name = "label5";
            this.label5.Size = new System.Drawing.Size(145, 13);
            this.label5.TabIndex = 5;
            this.label5.Text = "Переключить на предыдущий аккаунт:";
            // 
            // txtHotkeyScan
            // 
            this.txtHotkeyScan.Location = new System.Drawing.Point(9, 72);
            this.txtHotkeyScan.Name = "txtHotkeyScan";
            this.txtHotkeyScan.ReadOnly = true;
            this.txtHotkeyScan.Size = new System.Drawing.Size(339, 22);
            this.txtHotkeyScan.TabIndex = 7;
            this.txtHotkeyScan.KeyDown += new System.Windows.Forms.KeyEventHandler(this.txtHotkey_KeyDown);
            // 
            // label4
            // 
            this.label4.AutoSize = true;
            this.label4.Location = new System.Drawing.Point(9, 52);
            this.label4.Name = "label4";
            this.label4.Size = new System.Drawing.Size(87, 13);
            this.label4.TabIndex = 3;
            this.label4.Text = "Запустить скан QR:";
            // 
            // txtHotkeyToggle
            // 
            this.txtHotkeyToggle.Location = new System.Drawing.Point(9, 28);
            this.txtHotkeyToggle.Name = "txtHotkeyToggle";
            this.txtHotkeyToggle.ReadOnly = true;
            this.txtHotkeyToggle.Size = new System.Drawing.Size(339, 22);
            this.txtHotkeyToggle.TabIndex = 6;
            this.txtHotkeyToggle.KeyDown += new System.Windows.Forms.KeyEventHandler(this.txtHotkey_KeyDown);
            // 
            // label3
            // 
            this.label3.AutoSize = true;
            this.label3.Location = new System.Drawing.Point(9, 12);
            this.label3.Name = "label3";
            this.label3.Size = new System.Drawing.Size(123, 13);
            this.label3.TabIndex = 1;
            this.label3.Text = "Переключить режим QR-хоткеев:";
            // 
            // chkEnableQrHotkeys
            // 
            this.chkEnableQrHotkeys.AutoSize = true;
            this.chkEnableQrHotkeys.Location = new System.Drawing.Point(9, 292);
            this.chkEnableQrHotkeys.Name = "chkEnableQrHotkeys";
            this.chkEnableQrHotkeys.Size = new System.Drawing.Size(173, 17);
            this.chkEnableQrHotkeys.TabIndex = 5;
            this.chkEnableQrHotkeys.Text = "Запускать с включенными QR-хоткеями";
            this.chkEnableQrHotkeys.UseVisualStyleBackColor = true;
            // 
            // groupCloud
            // 
            this.groupCloud.Controls.Add(this.lblCloudLastSyncValue);
            this.groupCloud.Controls.Add(this.lblCloudLastSyncTitle);
            this.groupCloud.Controls.Add(this.btnCloudOpenBackups);
            this.groupCloud.Controls.Add(this.lblCloudStatus);
            this.groupCloud.Controls.Add(this.btnCloudPush);
            this.groupCloud.Controls.Add(this.btnCloudPull);
            this.groupCloud.Controls.Add(this.btnCloudTest);
            this.groupCloud.Controls.Add(this.chkSyncStoredCredentials);
            this.groupCloud.Controls.Add(this.txtWebDavPath);
            this.groupCloud.Controls.Add(this.label11);
            this.groupCloud.Controls.Add(this.txtWebDavPassword);
            this.groupCloud.Controls.Add(this.label10);
            this.groupCloud.Controls.Add(this.txtWebDavUsername);
            this.groupCloud.Controls.Add(this.label9);
            this.groupCloud.Controls.Add(this.txtWebDavUrl);
            this.groupCloud.Controls.Add(this.label8);
            this.groupCloud.Location = new System.Drawing.Point(12, 526);
            this.groupCloud.Name = "groupCloud";
            this.groupCloud.Size = new System.Drawing.Size(360, 392);
            this.groupCloud.TabIndex = 15;
            this.groupCloud.TabStop = false;
            this.groupCloud.Text = "Облачная синхронизация (WebDAV)";
            // 
            // lblCloudLastSyncValue
            // 
            this.lblCloudLastSyncValue.Location = new System.Drawing.Point(9, 358);
            this.lblCloudLastSyncValue.Name = "lblCloudLastSyncValue";
            this.lblCloudLastSyncValue.Size = new System.Drawing.Size(339, 28);
            this.lblCloudLastSyncValue.TabIndex = 15;
            this.lblCloudLastSyncValue.Text = "Never synced";
            // 
            // lblCloudLastSyncTitle
            // 
            this.lblCloudLastSyncTitle.AutoSize = true;
            this.lblCloudLastSyncTitle.Location = new System.Drawing.Point(9, 338);
            this.lblCloudLastSyncTitle.Name = "lblCloudLastSyncTitle";
            this.lblCloudLastSyncTitle.Size = new System.Drawing.Size(55, 13);
            this.lblCloudLastSyncTitle.TabIndex = 14;
            this.lblCloudLastSyncTitle.Text = "Last sync:";
            // 
            // btnCloudOpenBackups
            // 
            this.btnCloudOpenBackups.Location = new System.Drawing.Point(9, 290);
            this.btnCloudOpenBackups.Name = "btnCloudOpenBackups";
            this.btnCloudOpenBackups.Size = new System.Drawing.Size(339, 30);
            this.btnCloudOpenBackups.TabIndex = 22;
            this.btnCloudOpenBackups.Text = "Open backups";
            this.btnCloudOpenBackups.UseVisualStyleBackColor = true;
            this.btnCloudOpenBackups.Click += new System.EventHandler(this.btnCloudOpenBackups_Click);
            // 
            // lblCloudStatus
            // 
            this.lblCloudStatus.Location = new System.Drawing.Point(9, 326);
            this.lblCloudStatus.Name = "lblCloudStatus";
            this.lblCloudStatus.Size = new System.Drawing.Size(339, 16);
            this.lblCloudStatus.TabIndex = 12;
            this.lblCloudStatus.Text = "Сначала проверьте соединение, потом Pull или Push.";
            // 
            // btnCloudPush
            // 
            this.btnCloudPush.Location = new System.Drawing.Point(238, 248);
            this.btnCloudPush.Name = "btnCloudPush";
            this.btnCloudPush.Size = new System.Drawing.Size(110, 28);
            this.btnCloudPush.TabIndex = 24;
            this.btnCloudPush.Text = "Отправить в облако";
            this.btnCloudPush.UseVisualStyleBackColor = true;
            this.btnCloudPush.Click += new System.EventHandler(this.btnCloudPush_Click);
            // 
            // btnCloudPull
            // 
            this.btnCloudPull.Location = new System.Drawing.Point(122, 248);
            this.btnCloudPull.Name = "btnCloudPull";
            this.btnCloudPull.Size = new System.Drawing.Size(110, 28);
            this.btnCloudPull.TabIndex = 23;
            this.btnCloudPull.Text = "Загрузить из облака";
            this.btnCloudPull.UseVisualStyleBackColor = true;
            this.btnCloudPull.Click += new System.EventHandler(this.btnCloudPull_Click);
            // 
            // btnCloudTest
            // 
            this.btnCloudTest.Location = new System.Drawing.Point(9, 248);
            this.btnCloudTest.Name = "btnCloudTest";
            this.btnCloudTest.Size = new System.Drawing.Size(107, 28);
            this.btnCloudTest.TabIndex = 22;
            this.btnCloudTest.Text = "Проверить соединение";
            this.btnCloudTest.UseVisualStyleBackColor = true;
            this.btnCloudTest.Click += new System.EventHandler(this.btnCloudTest_Click);
            // 
            // chkSyncStoredCredentials
            // 
            this.chkSyncStoredCredentials.AutoSize = true;
            this.chkSyncStoredCredentials.Location = new System.Drawing.Point(9, 213);
            this.chkSyncStoredCredentials.Name = "chkSyncStoredCredentials";
            this.chkSyncStoredCredentials.Size = new System.Drawing.Size(284, 17);
            this.chkSyncStoredCredentials.TabIndex = 21;
            this.chkSyncStoredCredentials.Text = "Синхронизировать сохраненные логины SDA++";
            this.chkSyncStoredCredentials.UseVisualStyleBackColor = true;
            // 
            // txtWebDavPath
            // 
            this.txtWebDavPath.Location = new System.Drawing.Point(9, 179);
            this.txtWebDavPath.Name = "txtWebDavPath";
            this.txtWebDavPath.Size = new System.Drawing.Size(339, 22);
            this.txtWebDavPath.TabIndex = 20;
            // 
            // label11
            // 
            this.label11.AutoSize = true;
            this.label11.Location = new System.Drawing.Point(9, 161);
            this.label11.Name = "label11";
            this.label11.Size = new System.Drawing.Size(124, 13);
            this.label11.TabIndex = 7;
            this.label11.Text = "Путь к папке в облаке:";
            // 
            // txtWebDavPassword
            // 
            this.txtWebDavPassword.Location = new System.Drawing.Point(9, 127);
            this.txtWebDavPassword.Name = "txtWebDavPassword";
            this.txtWebDavPassword.PasswordChar = '*';
            this.txtWebDavPassword.Size = new System.Drawing.Size(339, 22);
            this.txtWebDavPassword.TabIndex = 19;
            // 
            // label10
            // 
            this.label10.AutoSize = true;
            this.label10.Location = new System.Drawing.Point(9, 109);
            this.label10.Name = "label10";
            this.label10.Size = new System.Drawing.Size(111, 13);
            this.label10.TabIndex = 5;
            this.label10.Text = "Пароль WebDAV:";
            // 
            // txtWebDavUsername
            // 
            this.txtWebDavUsername.Location = new System.Drawing.Point(9, 75);
            this.txtWebDavUsername.Name = "txtWebDavUsername";
            this.txtWebDavUsername.Size = new System.Drawing.Size(339, 22);
            this.txtWebDavUsername.TabIndex = 18;
            // 
            // label9
            // 
            this.label9.AutoSize = true;
            this.label9.Location = new System.Drawing.Point(9, 57);
            this.label9.Name = "label9";
            this.label9.Size = new System.Drawing.Size(109, 13);
            this.label9.TabIndex = 3;
            this.label9.Text = "Логин WebDAV:";
            // 
            // txtWebDavUrl
            // 
            this.txtWebDavUrl.Location = new System.Drawing.Point(9, 31);
            this.txtWebDavUrl.Name = "txtWebDavUrl";
            this.txtWebDavUrl.Size = new System.Drawing.Size(339, 22);
            this.txtWebDavUrl.TabIndex = 17;
            // 
            // label8
            // 
            this.label8.AutoSize = true;
            this.label8.Location = new System.Drawing.Point(9, 13);
            this.label8.Name = "label8";
            this.label8.Size = new System.Drawing.Size(80, 13);
            this.label8.TabIndex = 1;
            this.label8.Text = "URL WebDAV:";
            // 
            // SettingsForm
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(384, 984);
            this.Controls.Add(this.groupCloud);
            this.Controls.Add(this.groupQr);
            this.Controls.Add(this.chkConfirmTrades);
            this.Controls.Add(this.chkConfirmMarket);
            this.Controls.Add(this.chkCheckAll);
            this.Controls.Add(this.cmbLanguage);
            this.Controls.Add(this.labelLanguage);
            this.Controls.Add(this.label1);
            this.Controls.Add(this.numPeriodicInterval);
            this.Controls.Add(this.btnSave);
            this.Controls.Add(this.chkPeriodicChecking);
            this.Font = new System.Drawing.Font("Segoe UI", 8.25F);
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedDialog;
            this.Icon = ((System.Drawing.Icon)(resources.GetObject("$this.Icon")));
            this.MaximizeBox = false;
            this.Name = "SettingsForm";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
            this.Text = "Настройки";
            ((System.ComponentModel.ISupportInitialize)(this.numPeriodicInterval)).EndInit();
            this.groupQr.ResumeLayout(false);
            this.groupQr.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)(this.numCursorScanSize)).EndInit();
            this.groupCloud.ResumeLayout(false);
            this.groupCloud.PerformLayout();
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.CheckBox chkPeriodicChecking;
        private System.Windows.Forms.Button btnSave;
        private System.Windows.Forms.NumericUpDown numPeriodicInterval;
        private System.Windows.Forms.Label label1;
        private System.Windows.Forms.CheckBox chkCheckAll;
        private System.Windows.Forms.CheckBox chkConfirmMarket;
        private System.Windows.Forms.CheckBox chkConfirmTrades;
        private System.Windows.Forms.GroupBox groupQr;
        private System.Windows.Forms.CheckBox chkEnableQrHotkeys;
        private System.Windows.Forms.TextBox txtHotkeyToggle;
        private System.Windows.Forms.Label label3;
        private System.Windows.Forms.TextBox txtHotkeyScan;
        private System.Windows.Forms.Label label4;
        private System.Windows.Forms.TextBox txtHotkeyPrev;
        private System.Windows.Forms.Label label5;
        private System.Windows.Forms.TextBox txtHotkeyNext;
        private System.Windows.Forms.Label label6;
        private System.Windows.Forms.ComboBox cmbQrCaptureMode;
        private System.Windows.Forms.Label label7;
        private System.Windows.Forms.NumericUpDown numCursorScanSize;
        private System.Windows.Forms.Label lblCursorScanSize;
        private System.Windows.Forms.Button btnResetHotkeys;
        private System.Windows.Forms.GroupBox groupCloud;
        private System.Windows.Forms.Label lblCloudLastSyncValue;
        private System.Windows.Forms.Label lblCloudLastSyncTitle;
        private System.Windows.Forms.Button btnCloudOpenBackups;
        private System.Windows.Forms.Label lblCloudStatus;
        private System.Windows.Forms.Button btnCloudPush;
        private System.Windows.Forms.Button btnCloudPull;
        private System.Windows.Forms.Button btnCloudTest;
        private System.Windows.Forms.CheckBox chkSyncStoredCredentials;
        private System.Windows.Forms.TextBox txtWebDavPath;
        private System.Windows.Forms.Label label11;
        private System.Windows.Forms.TextBox txtWebDavPassword;
        private System.Windows.Forms.Label label10;
        private System.Windows.Forms.TextBox txtWebDavUsername;
        private System.Windows.Forms.Label label9;
        private System.Windows.Forms.TextBox txtWebDavUrl;
        private System.Windows.Forms.Label label8;
        private System.Windows.Forms.Label labelLanguage;
        private System.Windows.Forms.ComboBox cmbLanguage;
    }
}
