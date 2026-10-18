using SteamAuth;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
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
        private readonly Button activity = new Button();
        private readonly Button lockDevice = new Button();
        private readonly Button terminateSessions = new Button();
        private readonly Button notifications = new Button();
        private DesktopRegistrySettings settings;

        public DeviceRegistryForm()
        {
            Text = Localizer.Choose("Trusted devices", "Доверенные устройства");
            ClientSize = new Size(620, 540);
            StartPosition = FormStartPosition.CenterParent;
            BackColor = Branding.WindowBackground;
            ForeColor = Branding.HeadingText;
            Icon = Branding.LoadAppIcon();
            ModernUi.AttachWindowChrome(this, false, false);

            status.SetBounds(20, 48, 580, 60);
            status.ForeColor = Branding.MutedText;
            devices.SetBounds(20, 114, 580, 280);
            devices.View = View.Details;
            devices.FullRowSelect = true;
            devices.BackColor = Branding.CardBackground;
            devices.ForeColor = Branding.HeadingText;
            devices.Columns.Add(Localizer.Choose("Device", "Устройство"), 170);
            devices.Columns.Add(Localizer.Choose("Role", "Роль"), 105);
            devices.Columns.Add(Localizer.Choose("Last seen", "Последняя связь"), 140);
            devices.Columns.Add(Localizer.Choose("Expires", "Истекает"), 140);
            setup = MakeButton(Localizer.Choose("Create Admin registry", "Создать Admin-реестр"), 20, 410, 180, true);
            join = MakeButton(Localizer.Choose("Join by QR", "Подключить по QR"), 210, 410, 150, false);
            add = MakeButton(Localizer.Choose("Add device", "Добавить устройство"), 20, 410, 120, true);
            refresh = MakeButton(Localizer.Choose("Refresh", "Обновить"), 150, 410, 100, false);
            revoke = MakeButton(Localizer.Choose("Revoke", "Отозвать"), 260, 410, 90, false);
            wipe = MakeButton(Localizer.Choose("Wipe", "Очистить"), 360, 410, 90, false);
            lockdown = MakeButton(Localizer.Choose("Lockdown", "Аварийно"), 460, 410, 140, false);
            activity = MakeButton(Localizer.Choose("Security activity", "События безопасности"), 20, 456, 180, false);
            lockDevice = MakeButton(Localizer.Choose("Lock device", "Заблокировать"), 210, 456, 120, false);
            terminateSessions = MakeButton(Localizer.Choose("End sessions", "Завершить сессии"), 340, 456, 160, false);
            notifications = MakeButton(Localizer.Choose("Alerts", "Уведомления"), 510, 456, 90, false);
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
            activity.Click += (_, __) => new DeviceAuditForm(settings).ShowDialog(this);
            lockDevice.Click += async (_, __) => await IssueDeviceCommandAsync("Lock");
            terminateSessions.Click += async (_, __) => await IssueDeviceCommandAsync("TerminateSteamSessions");
            notifications.Click += (_, __) => new SecurityWebhookForm(settings).ShowDialog(this);
            Controls.AddRange(new Control[] { status, devices, setup, join, add, refresh, revoke, wipe, lockdown, activity, lockDevice, terminateSessions, notifications });
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
            DesktopAdminStepUp.ResetIfRegistryWasNotCreated(settings);
            bool configured = !string.IsNullOrWhiteSpace(settings.VaultId);
            setup.Visible = !configured;
            join.Visible = !configured;
            add.Visible = refresh.Visible = revoke.Visible = wipe.Visible = lockdown.Visible = activity.Visible =
                lockDevice.Visible = terminateSessions.Visible = notifications.Visible = configured && settings.Role == "Admin";
            devices.Visible = configured && settings.Role == "Admin";
            status.Text = BuildRegistryStatus(configured);
            if (configured && settings.Role == "Admin") await RefreshAsync();
            else if (configured) await client.HeartbeatAsync(settings, CancellationToken.None);
        }

        private async Task SetupAsync()
        {
            bool createdPinForThisAttempt = false;
            try
            {
                settings = DesktopRegistrySettingsStore.Load();
                createdPinForThisAttempt = !DesktopAdminStepUp.IsConfigured(settings);
                if (!DesktopAdminStepUp.Require(this, settings)) return;
                SetBusy(true);
                Manifest manifest = Manifest.GetManifest();
                string endpoint = manifest.PairingRelayMode == PairingRelayMode.Personal ? manifest.PairingRelayUrl : PairingRelayProfile.ManagedEndpoint;
                settings = await client.BootstrapAdminAsync(endpoint, Environment.MachineName, CancellationToken.None);
                await LoadAsync();
            }
            catch (Exception ex)
            {
                if (createdPinForThisAttempt && string.IsNullOrWhiteSpace(settings?.VaultId))
                {
                    DesktopAdminStepUp.ClearPin(settings);
                    DesktopRegistrySettingsStore.Save(settings);
                }
                MessageBox.Show(ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
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
                    item.SubItems.Add(device.LockedAt != null
                        ? device.Role + " · " + Localizer.Choose("Locked", "Заблокировано")
                        : device.Role);
                    item.SubItems.Add(DateTimeOffset.FromUnixTimeMilliseconds(device.LastSeenAt).LocalDateTime.ToString("g"));
                    item.SubItems.Add(FormatTimestamp(device.ExpiresAt));
                    if (device.RevokedAt != null || device.LockedAt != null) item.ForeColor = Branding.Danger;
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
            MessageBox.Show(Localizer.Choose($"Locked devices: {count}", $"Заблокировано устройств: {count}"), Text);
            await RefreshAsync();
        }

        private async Task WipeAsync()
        {
            if (devices.SelectedItems.Count == 0 || devices.SelectedItems[0].Tag is not DesktopRegistryDevice target) return;
            if (target.Role == "Admin")
            {
                MessageBox.Show(Localizer.Choose("Remote commands apply only to Trusted and Temporary devices.", "Удалённые команды применяются только к Trusted и Temporary устройствам."), Text);
                return;
            }
            if (!DesktopAdminStepUp.Require(this, settings)) return;
            if (MessageBox.Show(Localizer.Choose("Queue local data removal on this device?", "Поставить в очередь удаление локальных данных на этом устройстве?"), Text, MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) != DialogResult.OK) return;
            await client.IssueCommandAsync(settings, target.DeviceId, "WipeLocalData", CancellationToken.None);
            MessageBox.Show(Localizer.Choose("Wipe command queued.", "Команда очистки поставлена в очередь."), Text);
        }

        private async Task IssueDeviceCommandAsync(string command)
        {
            if (devices.SelectedItems.Count == 0 || devices.SelectedItems[0].Tag is not DesktopRegistryDevice target) return;
            if (target.Role == "Admin")
            {
                MessageBox.Show(Localizer.Choose("Remote commands apply only to Trusted and Temporary devices.", "Удалённые команды применяются только к Trusted и Temporary устройствам."), Text);
                return;
            }
            if (!DesktopAdminStepUp.Require(this, settings)) return;
            await client.IssueCommandAsync(settings, target.DeviceId, command, CancellationToken.None);
            MessageBox.Show(
                command == "Lock"
                    ? Localizer.Choose("Lock command queued.", "Команда блокировки поставлена в очередь.")
                    : Localizer.Choose("Steam session termination command queued.", "Команда завершения Steam-сессий поставлена в очередь."),
                Text);
        }

        private void SetBusy(bool busy) => setup.Enabled = join.Enabled = add.Enabled = refresh.Enabled = revoke.Enabled = wipe.Enabled =
            lockdown.Enabled = activity.Enabled = lockDevice.Enabled = terminateSessions.Enabled = notifications.Enabled = !busy;

        private string BuildRegistryStatus(bool configured)
        {
            if (!configured)
                return Localizer.Choose("Create a registry before adding trusted devices.", "Создайте реестр перед добавлением доверенных устройств.");
            if (settings.Role == "Admin")
                return Localizer.Choose("Signed Admin registry is active on this PC.", "Подписанный Admin-реестр активен на этом ПК.");

            string summary = Localizer.Choose($"This PC is enrolled as {settings.Role}.", $"Этот ПК подключён как {settings.Role}.");
            if (settings.LastSuccessfulHeartbeatAt != null)
            {
                summary += Environment.NewLine + Localizer.Choose("Last contact: ", "Последняя связь: ") + FormatTimestamp(settings.LastSuccessfulHeartbeatAt);
                if (settings.OfflineWipeAfterHours != null)
                {
                    long wipeAt = settings.LastSuccessfulHeartbeatAt.Value + (long)TimeSpan.FromHours(settings.OfflineWipeAfterHours.Value).TotalMilliseconds;
                    summary += Environment.NewLine + Localizer.Choose("Local wipe after: ", "Локальная очистка после: ") + FormatTimestamp(wipeAt);
                }
            }
            if (settings.ExpiresAt != null)
                summary += Environment.NewLine + Localizer.Choose("Access expires: ", "Доступ истекает: ") + FormatTimestamp(settings.ExpiresAt);
            return summary;
        }

        private static string FormatTimestamp(long? timestamp) => timestamp == null
            ? "-"
            : DateTimeOffset.FromUnixTimeMilliseconds(timestamp.Value).LocalDateTime.ToString("g");
    }

    internal sealed class SecurityWebhookForm : Form
    {
        private readonly DesktopRegistrySettings settings;
        private readonly DesktopDeviceRegistryClient client = new DesktopDeviceRegistryClient();
        private readonly Label status = new Label();
        private readonly TextBox endpoint = new TextBox();
        private readonly Button configure = new Button();
        private readonly Button test = new Button();
        private readonly Button remove = new Button();

        public SecurityWebhookForm(DesktopRegistrySettings settings)
        {
            this.settings = settings;
            Text = Localizer.Choose("Security notifications", "Уведомления безопасности");
            ClientSize = new Size(540, 300);
            StartPosition = FormStartPosition.CenterParent;
            BackColor = Branding.WindowBackground;
            ForeColor = Branding.HeadingText;
            Icon = Branding.LoadAppIcon();
            ModernUi.AttachWindowChrome(this, false, false);

            Label hint = new Label
            {
                Text = Localizer.Choose(
                    "Send signed security events to your HTTPS webhook. Steam secrets, passwords, and Guard codes are never included.",
                    "Отправлять подписанные события безопасности на HTTPS webhook. Steam-секреты, пароли и коды не передаются."),
                ForeColor = Branding.MutedText,
                Bounds = new Rectangle(20, 50, 500, 42)
            };
            status.SetBounds(20, 98, 500, 44);
            status.ForeColor = Branding.MutedText;
            endpoint.SetBounds(20, 150, 500, 30);
            endpoint.PlaceholderText = "https://example.com/sdapp-events";
            endpoint.BackColor = Branding.CardBackground;
            endpoint.ForeColor = Branding.HeadingText;
            endpoint.BorderStyle = BorderStyle.FixedSingle;
            configure.SetBounds(20, 198, 155, 34);
            configure.Text = Localizer.Choose("Configure", "Настроить");
            test.SetBounds(185, 198, 155, 34);
            test.Text = Localizer.Choose("Test", "Проверить");
            remove.SetBounds(350, 198, 170, 34);
            remove.Text = Localizer.Choose("Remove webhook", "Удалить webhook");
            ModernUi.RoundButton(configure, true);
            ModernUi.RoundButton(test, false);
            ModernUi.RoundButton(remove, false);
            configure.Click += async (_, __) => await ConfigureAsync();
            test.Click += async (_, __) => await TestAsync();
            remove.Click += async (_, __) => await RemoveAsync();
            Controls.AddRange(new Control[] { hint, status, endpoint, configure, test, remove });
            Paint += ModernUi.PaintGlassBackground;
            Shown += async (_, __) => await RefreshAsync();
        }

        private async Task RefreshAsync()
        {
            try
            {
                SetBusy(true);
                DesktopSecurityWebhookStatus snapshot = await client.GetSecurityWebhookAsync(settings, CancellationToken.None);
                status.Text = BuildStatus(snapshot);
                test.Enabled = remove.Enabled = snapshot.Configured;
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                configure.Enabled = true;
                if (string.IsNullOrWhiteSpace(status.Text)) test.Enabled = remove.Enabled = false;
            }
        }

        private async Task ConfigureAsync()
        {
            if (string.IsNullOrWhiteSpace(endpoint.Text)) return;
            if (!DesktopAdminStepUp.Require(this, settings)) return;
            byte[] secretBytes = RandomNumberGenerator.GetBytes(32);
            string signingSecret = Convert.ToBase64String(secretBytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
            CryptographicOperations.ZeroMemory(secretBytes);
            try
            {
                SetBusy(true);
                await client.ConfigureSecurityWebhookAsync(settings, endpoint.Text, signingSecret, CancellationToken.None);
                using WebhookSecretForm secret = new WebhookSecretForm(signingSecret);
                secret.ShowDialog(this);
                await RefreshAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally { SetBusy(false); }
        }

        private async Task TestAsync()
        {
            try
            {
                SetBusy(true);
                await client.TestSecurityWebhookAsync(settings, CancellationToken.None);
                await RefreshAsync();
                MessageBox.Show(
                    Localizer.Choose("Test notification sent.", "Тестовое уведомление отправлено."),
                    Text,
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }
            catch (Exception ex) { MessageBox.Show(ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error); }
            finally { SetBusy(false); }
        }

        private async Task RemoveAsync()
        {
            if (!DesktopAdminStepUp.Require(this, settings)) return;
            if (MessageBox.Show(
                Localizer.Choose("Remove the security webhook?", "Удалить webhook безопасности?"),
                Text,
                MessageBoxButtons.OKCancel,
                MessageBoxIcon.Warning) != DialogResult.OK) return;
            try
            {
                SetBusy(true);
                await client.RemoveSecurityWebhookAsync(settings, CancellationToken.None);
                status.Text = Localizer.Choose("Webhook is not configured.", "Webhook не настроен.");
                test.Enabled = remove.Enabled = false;
            }
            catch (Exception ex) { MessageBox.Show(ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error); }
            finally { SetBusy(false); }
        }

        private void SetBusy(bool busy) => configure.Enabled = test.Enabled = remove.Enabled = !busy;

        private static string BuildStatus(DesktopSecurityWebhookStatus value)
        {
            if (value?.Configured != true)
                return Localizer.Choose("Webhook is not configured.", "Webhook не настроен.");
            string result = Localizer.Choose("Configured for: ", "Настроен для: ") + (value.EndpointHost ?? "-");
            if (value.LastDeliveryAt != null)
            {
                result += Environment.NewLine + Localizer.Choose("Last delivery: ", "Последняя доставка: ") +
                    DateTimeOffset.FromUnixTimeMilliseconds(value.LastDeliveryAt.Value).LocalDateTime.ToString("g") + " · " +
                    (value.LastDeliveryOk == true
                        ? Localizer.Choose("successful", "успешно")
                        : Localizer.Choose("failed", "ошибка"));
            }
            return result;
        }
    }

    internal sealed class WebhookSecretForm : Form
    {
        private bool acknowledged;

        public WebhookSecretForm(string secret)
        {
            Text = Localizer.Choose("Save signing secret", "Сохраните секрет подписи");
            ClientSize = new Size(540, 300);
            StartPosition = FormStartPosition.CenterParent;
            BackColor = Branding.WindowBackground;
            ForeColor = Branding.HeadingText;
            Icon = Branding.LoadAppIcon();
            ModernUi.AttachWindowChrome(this, false, false);
            Label warning = new Label
            {
                Text = Localizer.Choose(
                    "This secret is shown once. Your receiver needs it to verify the HMAC-SHA256 signature. SDA++ cannot display it again.",
                    "Секрет показывается один раз. Он нужен обработчику для проверки HMAC-SHA256 подписи. SDA++ не сможет показать его повторно."),
                ForeColor = Branding.MutedText,
                Bounds = new Rectangle(20, 52, 500, 58)
            };
            TextBox value = new TextBox
            {
                Text = secret,
                ReadOnly = true,
                Multiline = true,
                Bounds = new Rectangle(20, 120, 500, 54),
                BackColor = Branding.CardBackground,
                ForeColor = Branding.HeadingText,
                BorderStyle = BorderStyle.FixedSingle
            };
            Button copy = new Button
            {
                Text = Localizer.Choose("Copy secret", "Копировать секрет"),
                Bounds = new Rectangle(20, 195, 190, 34)
            };
            Button saved = new Button
            {
                Text = Localizer.Choose("I saved the secret", "Я сохранил секрет"),
                Bounds = new Rectangle(220, 195, 300, 34)
            };
            ModernUi.RoundButton(copy, false);
            ModernUi.RoundButton(saved, true);
            copy.Click += (_, __) => Clipboard.SetText(secret);
            saved.Click += (_, __) =>
            {
                acknowledged = true;
                Close();
            };
            FormClosing += (_, e) => e.Cancel = !acknowledged;
            Controls.AddRange(new Control[] { warning, value, copy, saved });
            Paint += ModernUi.PaintGlassBackground;
        }
    }

    internal sealed class DeviceAuditForm : Form
    {
        private readonly DesktopRegistrySettings settings;
        private readonly ListView events = new ListView();

        public DeviceAuditForm(DesktopRegistrySettings settings)
        {
            this.settings = settings;
            Text = Localizer.Choose("Security activity", "События безопасности");
            ClientSize = new Size(680, 460);
            StartPosition = FormStartPosition.CenterParent;
            BackColor = Branding.WindowBackground;
            ForeColor = Branding.HeadingText;
            Icon = Branding.LoadAppIcon();
            ModernUi.AttachWindowChrome(this, false, false);
            events.SetBounds(20, 48, 640, 390);
            events.View = View.Details;
            events.FullRowSelect = true;
            events.BackColor = Branding.CardBackground;
            events.ForeColor = Branding.HeadingText;
            events.Columns.Add(Localizer.Choose("Time", "Время"), 145);
            events.Columns.Add(Localizer.Choose("Action", "Действие"), 190);
            events.Columns.Add(Localizer.Choose("Actor", "Инициатор"), 105);
            events.Columns.Add(Localizer.Choose("Target", "Цель"), 105);
            events.Columns.Add(Localizer.Choose("Country", "Страна"), 70);
            Controls.Add(events);
            Paint += ModernUi.PaintGlassBackground;
            Shown += async (_, __) => await LoadEventsAsync();
        }

        private async Task LoadEventsAsync()
        {
            try
            {
                IReadOnlyList<DesktopRegistryAuditEvent> snapshot = await new DesktopDeviceRegistryClient()
                    .ListAuditAsync(settings, CancellationToken.None);
                events.Items.Clear();
                foreach (DesktopRegistryAuditEvent audit in snapshot.OrderByDescending(value => value.OccurredAt).Take(200))
                {
                    ListViewItem item = new ListViewItem(DateTimeOffset.FromUnixTimeMilliseconds(audit.OccurredAt).LocalDateTime.ToString("g"));
                    item.SubItems.Add(SplitAction(audit.Action));
                    item.SubItems.Add(ShortId(audit.ActorDeviceId));
                    item.SubItems.Add(ShortId(audit.TargetDeviceId));
                    item.SubItems.Add(audit.Country ?? string.Empty);
                    events.Items.Add(item);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private static string ShortId(string value) => string.IsNullOrWhiteSpace(value)
            ? "-"
            : value.Substring(0, Math.Min(8, value.Length));

        private static string SplitAction(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return "-";
            return System.Text.RegularExpressions.Regex.Replace(value, "([a-z])([A-Z])", "$1 $2");
        }
    }

    internal sealed class DeviceInvitationAccountChoice
    {
        public SteamGuardAccount Account { get; }
        public string AccountName { get; }
        public string SteamId { get; }

        public DeviceInvitationAccountChoice(SteamGuardAccount account)
        {
            Account = account ?? throw new ArgumentNullException(nameof(account));
            SteamId = account.Session?.SteamID > 0 ? account.Session.SteamID.ToString() : "-";
            AccountName = string.IsNullOrWhiteSpace(account.AccountName)
                ? Localizer.Choose("Unnamed account", "Аккаунт без имени")
                : account.AccountName.Trim();
        }

        public bool Matches(string query)
        {
            string value = query?.Trim() ?? string.Empty;
            return value.Length == 0 ||
                AccountName.IndexOf(value, StringComparison.OrdinalIgnoreCase) >= 0 ||
                SteamId.IndexOf(value, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        public override string ToString() => $"{AccountName}  ·  {SteamId}";
    }

    internal sealed class DeviceInvitationForm : Form
    {
        private readonly DesktopRegistrySettings settings;
        private readonly CheckedListBox accounts = new CheckedListBox();
        private readonly TextBox accountSearch = new TextBox();
        private readonly ComboBox role = new ComboBox();
        private readonly PictureBox qr = new PictureBox();
        private readonly Label hint = new Label();
        private readonly List<DeviceInvitationAccountChoice> availableAccounts = new List<DeviceInvitationAccountChoice>();
        private readonly HashSet<SteamGuardAccount> selectedAccounts = new HashSet<SteamGuardAccount>();

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
                accountSearch.Enabled = role.SelectedIndex != 3;
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
            accountSearch.SetBounds(24, 132, 342, 28);
            accountSearch.PlaceholderText = Localizer.Choose("Search by account name or SteamID", "Поиск по имени аккаунта или SteamID");
            accountSearch.BackColor = Branding.CardBackground;
            accountSearch.ForeColor = Branding.HeadingText;
            accountSearch.BorderStyle = BorderStyle.FixedSingle;
            accountSearch.TextChanged += (_, __) => ApplyAccountFilter();
            accounts.SetBounds(24, 168, 342, 258);
            accounts.BackColor = Branding.CardBackground;
            accounts.ForeColor = Branding.HeadingText;
            accounts.CheckOnClick = true;
            accounts.ItemCheck += (_, args) =>
            {
                if (args.Index < 0 || args.Index >= accounts.Items.Count) return;
                DeviceInvitationAccountChoice choice = accounts.Items[args.Index] as DeviceInvitationAccountChoice;
                if (choice == null) return;
                if (args.NewValue == CheckState.Checked) selectedAccounts.Add(choice.Account);
                else selectedAccounts.Remove(choice.Account);
            };
            Manifest manifest = Manifest.GetManifest(true);
            string passKey = manifest.Encrypted ? manifest.PromptForPassKey() : null;
            SteamGuardAccount[] available = manifest.Encrypted && passKey == null
                ? Array.Empty<SteamGuardAccount>()
                : manifest.GetAllAccounts(passKey) ?? Array.Empty<SteamGuardAccount>();
            availableAccounts.AddRange(available
                .Select(account => new DeviceInvitationAccountChoice(account))
                .OrderBy(choice => choice.AccountName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(choice => choice.SteamId, StringComparer.Ordinal));
            foreach (DeviceInvitationAccountChoice choice in availableAccounts) selectedAccounts.Add(choice.Account);
            ApplyAccountFilter();
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
            Controls.AddRange(new Control[] { hint, role, accountSearch, accounts, qr, create });
            Paint += ModernUi.PaintGlassBackground;
        }

        private void ApplyAccountFilter()
        {
            accounts.BeginUpdate();
            try
            {
                accounts.Items.Clear();
                foreach (DeviceInvitationAccountChoice choice in availableAccounts.Where(item => item.Matches(accountSearch.Text)))
                {
                    accounts.Items.Add(choice, selectedAccounts.Contains(choice.Account));
                }
            }
            finally { accounts.EndUpdate(); }
        }

        private async Task CreateAsync(Button button)
        {
            List<SteamGuardAccount> selected = availableAccounts
                .Where(choice => selectedAccounts.Contains(choice.Account))
                .Select(choice => choice.Account)
                .ToList();
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
                accountSearch.Visible = false;
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
