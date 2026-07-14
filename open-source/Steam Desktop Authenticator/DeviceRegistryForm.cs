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
        private readonly Button add = new Button();
        private readonly Button refresh = new Button();
        private readonly Button revoke = new Button();
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
            add = MakeButton(Localizer.Choose("Add device", "Добавить устройство"), 20, 410, 120, true);
            refresh = MakeButton(Localizer.Choose("Refresh", "Обновить"), 150, 410, 100, false);
            revoke = MakeButton(Localizer.Choose("Revoke", "Отозвать"), 260, 410, 100, false);
            lockdown = MakeButton(Localizer.Choose("Emergency lockdown", "Аварийный отзыв"), 370, 410, 230, false);
            setup.Click += async (_, __) => await SetupAsync();
            add.Click += (_, __) => new DeviceInvitationForm(settings).ShowDialog(this);
            refresh.Click += async (_, __) => await RefreshAsync();
            revoke.Click += async (_, __) => await RevokeAsync();
            lockdown.Click += async (_, __) => await LockdownAsync();
            Controls.AddRange(new Control[] { status, devices, setup, add, refresh, revoke, lockdown });
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
            add.Visible = refresh.Visible = revoke.Visible = lockdown.Visible = configured && settings.Role == "Admin";
            devices.Visible = configured;
            status.Text = configured
                ? Localizer.Choose("Signed Admin registry is active on this PC.", "Подписанный Admin-реестр активен на этом ПК.")
                : Localizer.Choose("Create a registry before adding trusted devices.", "Создайте реестр перед добавлением доверенных устройств.");
            if (configured) await RefreshAsync();
        }

        private async Task SetupAsync()
        {
            try
            {
                SetBusy(true);
                Manifest manifest = Manifest.GetManifest();
                string endpoint = manifest.PairingRelayMode == PairingRelayMode.Personal ? manifest.PairingRelayUrl : PairingRelayProfile.ManagedEndpoint;
                settings = await client.BootstrapAdminAsync(endpoint, Environment.MachineName, CancellationToken.None);
                await LoadAsync();
            }
            catch (Exception ex) { MessageBox.Show(ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error); }
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
            if (MessageBox.Show(Localizer.Choose("Revoke this device?", "Отозвать это устройство?"), Text, MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) != DialogResult.OK) return;
            await client.RevokeAsync(settings, target.DeviceId, CancellationToken.None);
            await RefreshAsync();
        }

        private async Task LockdownAsync()
        {
            if (MessageBox.Show(Localizer.Choose("Revoke every non-Admin device now?", "Отозвать все не-Admin устройства сейчас?"), Text, MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) != DialogResult.OK) return;
            int count = await client.LockdownAsync(settings, CancellationToken.None);
            MessageBox.Show(Localizer.Choose($"Revoked devices: {count}", $"Отозвано устройств: {count}"), Text);
            await RefreshAsync();
        }

        private void SetBusy(bool busy) => setup.Enabled = add.Enabled = refresh.Enabled = revoke.Enabled = lockdown.Enabled = !busy;
    }

    internal sealed class DeviceInvitationForm : Form
    {
        private readonly DesktopRegistrySettings settings;
        private readonly CheckedListBox accounts = new CheckedListBox();
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
            accounts.SetBounds(24, 96, 342, 330);
            accounts.BackColor = Branding.CardBackground;
            accounts.ForeColor = Branding.HeadingText;
            SteamGuardAccount[] available = Manifest.GetManifest().GetAllAccounts() ?? Array.Empty<SteamGuardAccount>();
            foreach (SteamGuardAccount account in available) accounts.Items.Add(account, true);
            Button create = new Button { Text = Localizer.Choose("Create one-time QR", "Создать одноразовый QR"), Bounds = new Rectangle(24, 444, 342, 36) };
            ModernUi.RoundButton(create, true);
            create.Click += async (_, __) =>
            {
                if (create.Tag as string == "close") Close();
                else await CreateAsync(create);
            };
            qr.SetBounds(55, 112, 280, 280);
            qr.SizeMode = PictureBoxSizeMode.Zoom;
            qr.BackColor = Color.White;
            qr.Visible = false;
            Controls.AddRange(new Control[] { hint, accounts, qr, create });
            Paint += ModernUi.PaintGlassBackground;
        }

        private async Task CreateAsync(Button button)
        {
            List<SteamGuardAccount> selected = accounts.CheckedItems.Cast<SteamGuardAccount>().ToList();
            if (selected.Count == 0) return;
            button.Enabled = false;
            try
            {
                DesktopDeviceInvitation invitation = await new DesktopDeviceRegistryClient().CreateInvitationAsync(
                    settings,
                    selected.Select(account => account.Session?.SteamID.ToString()).Where(value => !string.IsNullOrWhiteSpace(value)),
                    new[] { "ViewGuardCode", "ApproveSteamQr", "ViewConfirmations", "ApproveConfirmations" },
                    CancellationToken.None);
                accounts.Visible = false;
                qr.Image = CreateQrBitmap(invitation.QrPayload, 280, 280);
                qr.Visible = true;
                hint.Text = Localizer.Choose("Scan in SDA++ Mobile. This QR expires in five minutes and works once.", "Отсканируйте в SDA++ Mobile. QR действует пять минут и только один раз.");
                button.Text = Localizer.Choose("Close", "Закрыть");
                button.Tag = "close";
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
