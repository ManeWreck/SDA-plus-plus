using SteamAuth;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using ZXing;
using ZXing.Common;
using ZXing.Rendering;

namespace Steam_Desktop_Authenticator
{
    internal sealed class DeviceRegistryForm : Form
    {
        private readonly DesktopDeviceRegistryClient client = new DesktopDeviceRegistryClient();
        private readonly ListView devices = new ListView();
        private readonly Label status = new Label();
        private readonly Button setup = new Button();
        private readonly Button join = new Button();
        private readonly Button add = new Button();
        private readonly Button refresh = new Button();
        private readonly Button revoke = new Button();
        private readonly Button wipe = new Button();
        private readonly Button lockdown = new Button();
        private DesktopRegistrySettings settings;

        public DeviceRegistryForm()
        {
            Text = Localizer.Choose("Trusted devices", "Доверенные устройства");
            ClientSize = new Size(620, 490);
            StartPosition = FormStartPosition.CenterParent;
            BackColor = Branding.WindowBackground;
            ForeColor = Branding.HeadingText;
            Icon = Branding.LoadAppIcon();
            ModernUi.AttachWindowChrome(this, false, false);

            status.SetBounds(20, 48, 580, 40);
            status.ForeColor = Branding.MutedText;
            devices.SetBounds(20, 94, 580, 300);
            devices.View = View.Details;
            devices.FullRowSelect = true;
            devices.BackColor = Branding.CardBackground;
            devices.ForeColor = Branding.HeadingText;
            devices.Columns.Add(Localizer.Choose("Device", "Устройство"), 210);
            devices.Columns.Add(Localizer.Choose("Role", "Роль"), 120);
            devices.Columns.Add(Localizer.Choose("Last seen", "Последняя связь"), 160);
            setup = MakeButton(Localizer.Choose("Create Admin registry", "Создать Admin-реестр"), 20, 410, 180, true);
            join = MakeButton(Localizer.Choose("Join by QR", "Подключить по QR"), 210, 410, 150, false);
            add = MakeButton(Localizer.Choose("Add device", "Добавить устройство"), 20, 410, 120, true);
            refresh = MakeButton(Localizer.Choose("Refresh", "Обновить"), 150, 410, 100, false);
            revoke = MakeButton(Localizer.Choose("Revoke", "Отозвать"), 260, 410, 90, false);
            wipe = MakeButton(Localizer.Choose("Wipe", "Очистить"), 360, 410, 90, false);
            lockdown = MakeButton(Localizer.Choose("Lockdown", "Аварийно"), 460, 410, 140, false);
            setup.Click += async (_, __) => await SetupAsync();
            join.Click += async (_, __) => await JoinAsync();
            add.Click += (_, __) =>
            {
                if (DesktopAdminStepUp.Require(this, settings)) new DeviceInvitationForm(settings).ShowDialog(this);
            };
            refresh.Click += async (_, __) => await RefreshAsync();
            revoke.Click += async (_, __) => await RevokeAsync();
            wipe.Click += async (_, __) => await WipeAsync();
            lockdown.Click += async (_, __) => await LockdownAsync();
            Controls.AddRange(new Control[] { status, devices, setup, join, add, refresh, revoke, wipe, lockdown });
            Paint += ModernUi.PaintGlassBackground;
            Shown += async (_, __) => await LoadAsync();
        }

        private Button MakeButton(string text, int x, int y, int width, bool primary)
        {
            Button result = new Button { Text = text, Bounds = new Rectangle(x, y, width, 34) };
            ModernUi.RoundButton(result, primary);
            return result;
        }

        private async Task LoadAsync()
        {
            settings = DesktopRegistrySettingsStore.Load();
            bool configured = !string.IsNullOrWhiteSpace(settings.VaultId);
            setup.Visible = !configured;
            join.Visible = !configured;
            add.Visible = refresh.Visible = revoke.Visible = wipe.Visible = lockdown.Visible = configured && settings.Role == "Admin";
            devices.Visible = configured && settings.Role == "Admin";
            status.Text = configured
                ? settings.Role == "Admin"
                    ? Localizer.Choose("Signed Admin registry is active on this PC.", "Подписанный Admin-реестр активен на этом ПК.")
                    : Localizer.Choose($"This PC is enrolled as {settings.Role}.", $"Этот ПК подключён как {settings.Role}.")
                : Localizer.Choose("Create a registry before adding trusted devices.", "Создайте реестр перед добавлением доверенных устройств.");
            if (configured && settings.Role == "Admin") await RefreshAsync();
            else if (configured) await client.HeartbeatAsync(settings, CancellationToken.None);
        }

        private async Task SetupAsync()
        {
            try
            {
                settings = DesktopRegistrySettingsStore.Load();
                if (!DesktopAdminStepUp.Require(this, settings)) return;
                SetBusy(true);
                Manifest manifest = Manifest.GetManifest();
                string endpoint = manifest.PairingRelayMode == PairingRelayMode.Personal ? manifest.PairingRelayUrl : PairingRelayProfile.ManagedEndpoint;
                settings = await client.BootstrapAdminAsync(endpoint, Environment.MachineName, CancellationToken.None);
                await LoadAsync();
            }
            catch (Exception ex) { MessageBox.Show(ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error); }
            finally { SetBusy(false); }
        }

        private async Task JoinAsync()
        {
            try
            {
                SetBusy(true);
                Hide();
                await Task.Delay(250);
                Manifest manifest = Manifest.GetManifest();
                string payload = new SteamQrLoginService().CaptureAndDecodeSteamQr(manifest.QrCaptureMode, manifest.QrCursorScanSize);
                Show();
                Activate();
                using InputForm code = new InputForm(Localizer.Choose(
                    "Enter the 6-digit device verification code shown by the Admin device.",
                    "Введите 6-значный код устройства, показанный на Admin-устройстве."));
                code.ShowDialog(this);
                if (code.Canceled) return;
                string vaultPassKey = null;
                Manifest localManifest = Manifest.GetManifest(true);
                if (localManifest.Encrypted)
                {
                    vaultPassKey = localManifest.PromptForPassKey();
                    if (vaultPassKey == null) return;
                }
                settings = await client.ClaimInvitationAsync(payload, code.txtBox.Text, vaultPassKey, CancellationToken.None);
                await LoadAsync();
            }
            catch (Exception ex)
            {
                if (!Visible) Show();
                MessageBox.Show(ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally { SetBusy(false); }
        }

        private async Task RefreshAsync()
        {
            try
            {
                SetBusy(true);
                IReadOnlyList<DesktopRegistryDevice> snapshot = await client.ListDevicesAsync(settings, CancellationToken.None);
                devices.Items.Clear();
                foreach (DesktopRegistryDevice device in snapshot)
                {
                    ListViewItem item = new ListViewItem(device.DisplayName ?? device.DeviceId) { Tag = device };
                    item.SubItems.Add(device.Role);
                    item.SubItems.Add(DateTimeOffset.FromUnixTimeMilliseconds(device.LastSeenAt).LocalDateTime.ToString("g"));
                    if (device.RevokedAt != null) item.ForeColor = Branding.Danger;
                    devices.Items.Add(item);
                }
            }
            catch (Exception ex) { MessageBox.Show(ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error); }
            finally { SetBusy(false); }
        }

        private async Task RevokeAsync()
        {
            if (devices.SelectedItems.Count == 0 || devices.SelectedItems[0].Tag is not DesktopRegistryDevice target) return;
            if (!DesktopAdminStepUp.Require(this, settings)) return;
            if (MessageBox.Show(Localizer.Choose("Revoke this device?", "Отозвать это устройство?"), Text, MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) != DialogResult.OK) return;
            await client.RevokeAsync(settings, target.DeviceId, CancellationToken.None);
            await RefreshAsync();
        }

        private async Task LockdownAsync()
        {
            if (!DesktopAdminStepUp.Require(this, settings)) return;
            if (MessageBox.Show(Localizer.Choose("Revoke every non-Admin device now?", "Отозвать все не-Admin устройства сейчас?"), Text, MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) != DialogResult.OK) return;
            int count = await client.LockdownAsync(settings, CancellationToken.None);
            MessageBox.Show(Localizer.Choose($"Revoked devices: {count}", $"Отозвано устройств: {count}"), Text);
            await RefreshAsync();
        }

        private async Task WipeAsync()
        {
            if (devices.SelectedItems.Count == 0 || devices.SelectedItems[0].Tag is not DesktopRegistryDevice target) return;
            if (!DesktopAdminStepUp.Require(this, settings)) return;
            if (MessageBox.Show(Localizer.Choose("Queue local data removal on this device?", "Поставить в очередь удаление локальных данных на этом устройстве?"), Text, MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) != DialogResult.OK) return;
            await client.IssueCommandAsync(settings, target.DeviceId, "WipeLocalData", CancellationToken.None);
            MessageBox.Show(Localizer.Choose("Wipe command queued.", "Команда очистки поставлена в очередь."), Text);
        }

        private void SetBusy(bool busy) => setup.Enabled = join.Enabled = add.Enabled = refresh.Enabled = revoke.Enabled = wipe.Enabled = lockdown.Enabled = !busy;
    }

    internal sealed class DeviceInvitationForm : Form
    {
        private readonly DesktopRegistrySettings settings;
        private readonly CheckedListBox accounts = new CheckedListBox();
        private readonly ComboBox role = new ComboBox();
        private readonly PictureBox qr = new PictureBox();
        private readonly Label hint = new Label();

        public DeviceInvitationForm(DesktopRegistrySettings settings)
        {
            this.settings = settings;
            Text = Localizer.Choose("Add trusted device", "Добавить доверенное устройство");
            ClientSize = new Size(390, 520);
            StartPosition = FormStartPosition.CenterParent;
            BackColor = Branding.WindowBackground;
            ForeColor = Branding.HeadingText;
            Icon = Branding.LoadAppIcon();
            ModernUi.AttachWindowChrome(this, false, false);
            hint.SetBounds(24, 48, 342, 42);
            hint.Text = Localizer.Choose("Choose accounts shared with this device.", "Выберите аккаунты, доступные этому устройству.");
            hint.ForeColor = Branding.MutedText;
            role.SetBounds(24, 96, 342, 28);
            role.DropDownStyle = ComboBoxStyle.DropDownList;
            role.Items.AddRange(new object[]
            {
                Localizer.Choose("Trusted online (permissions only)", "Trusted online (только разрешения)"),
                Localizer.Choose("Trusted offline (copies selected maFiles)", "Trusted offline (копирует выбранные maFile)"),
                Localizer.Choose("Temporary (24 hours, permissions only)", "Temporary (24 часа, только разрешения)"),
                Localizer.Choose("Admin (device management only)", "Admin (только управление устройствами)")
            });
            role.SelectedIndex = 0;
            role.SelectedIndexChanged += (_, __) =>
            {
                accounts.Enabled = role.SelectedIndex != 3;
                hint.Text = role.SelectedIndex switch
                {
                    1 => Localizer.Choose(
                        "Warning: remote wipe cannot revoke a Steam Guard secret already copied outside SDA++.",
                        "Внимание: удалённая очистка не отзовёт Steam Guard secret, уже скопированный вне SDA++."),
                    3 => Localizer.Choose(
                        "Admin can manage devices and recovery. No accounts or Steam secrets are transferred.",
                        "Admin может управлять устройствами и восстановлением. Аккаунты и Steam-секреты не передаются."),
                    _ => Localizer.Choose("Choose accounts shared with this device.", "Выберите аккаунты, доступные этому устройству.")
                };
            };
            accounts.SetBounds(24, 132, 342, 294);
            accounts.BackColor = Branding.CardBackground;
            accounts.ForeColor = Branding.HeadingText;
            Manifest manifest = Manifest.GetManifest(true);
            string passKey = manifest.Encrypted ? manifest.PromptForPassKey() : null;
            SteamGuardAccount[] available = manifest.Encrypted && passKey == null
                ? Array.Empty<SteamGuardAccount>()
                : manifest.GetAllAccounts(passKey) ?? Array.Empty<SteamGuardAccount>();
            foreach (SteamGuardAccount account in available) accounts.Items.Add(account, true);
            Button create = new Button { Text = Localizer.Choose("Create one-time QR", "Создать одноразовый QR"), Bounds = new Rectangle(24, 444, 342, 36) };
            ModernUi.RoundButton(create, true);
            create.Click += async (_, __) =>
            {
                if (create.Tag as string == "close") Close();
                else if (create.Tag as string == "show-code")
                {
                    qr.Visible = false;
                    hint.Text = Localizer.Choose("Verification code", "Код подтверждения") + ": " + create.AccessibleDescription;
                    create.Text = Localizer.Choose("Close", "Закрыть");
                    create.Tag = "close";
                }
                else await CreateAsync(create);
            };
            qr.SetBounds(55, 112, 280, 280);
            qr.SizeMode = PictureBoxSizeMode.Zoom;
            qr.BackColor = Color.White;
            qr.Visible = false;
            Controls.AddRange(new Control[] { hint, role, accounts, qr, create });
            Paint += ModernUi.PaintGlassBackground;
        }

        private async Task CreateAsync(Button button)
        {
            List<SteamGuardAccount> selected = accounts.CheckedItems.Cast<SteamGuardAccount>().ToList();
            bool creatingAdmin = role.SelectedIndex == 3;
            if (!creatingAdmin && selected.Count == 0) return;
            button.Enabled = false;
            try
            {
                string roleValue = role.SelectedIndex switch
                {
                    1 => "TrustedOffline",
                    2 => "Temporary",
                    3 => "Admin",
                    _ => "TrustedOnline"
                };
                DesktopDeviceInvitation invitation = await new DesktopDeviceRegistryClient().CreateInvitationAsync(
                    settings,
                    roleValue,
                    creatingAdmin ? Array.Empty<SteamGuardAccount>() : selected,
                    creatingAdmin
                        ? Array.Empty<string>()
                        : new[] { "ViewGuardCode", "ApproveSteamQr", "ViewConfirmations", "ApproveConfirmations" },
                    CancellationToken.None);
                accounts.Visible = false;
                role.Visible = false;
                qr.Image = CreateQrBitmap(invitation.QrPayload, 280, 280);
                qr.Visible = true;
                hint.Text = Localizer.Choose("Scan in SDA++ Mobile. This QR expires in five minutes and works once.", "Отсканируйте в SDA++ Mobile. QR действует пять минут и только один раз.");
                button.Text = Localizer.Choose("Show verification code", "Показать код подтверждения");
                button.AccessibleDescription = invitation.VerificationCode;
                button.Tag = "show-code";
                button.Enabled = true;
            }
            catch (Exception ex) { button.Enabled = true; MessageBox.Show(ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error); }
        }

        private static Bitmap CreateQrBitmap(string content, int width, int height)
        {
            var writer = new BarcodeWriterPixelData { Format = BarcodeFormat.QR_CODE, Options = new EncodingOptions { Width = width, Height = height, Margin = 2, PureBarcode = true } };
            PixelData pixels = writer.Write(content);
            Bitmap bitmap = new Bitmap(pixels.Width, pixels.Height, PixelFormat.Format32bppRgb);
            BitmapData data = bitmap.LockBits(new Rectangle(0, 0, pixels.Width, pixels.Height), ImageLockMode.WriteOnly, PixelFormat.Format32bppRgb);
            try { Marshal.Copy(pixels.Pixels, 0, data.Scan0, pixels.Pixels.Length); }
            finally { bitmap.UnlockBits(data); }
            return bitmap;
        }
    }
}
