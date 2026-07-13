using SteamAuth;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Steam_Desktop_Authenticator
{
    internal sealed class AccountMonitoringForm : Form
    {
        private readonly SteamGuardAccount[] accounts;
        private readonly AccountMonitoringService monitoringService = new AccountMonitoringService();
        private readonly DataGridView grid = new DataGridView();
        private readonly Button btnRefresh = new Button();
        private readonly Label lblStatus = new Label();
        private readonly TextBox txtSearch = new TextBox();
        private readonly ComboBox cmbFilter = new ComboBox();
        private readonly ComboBox cmbSort = new ComboBox();
        private AccountMonitoringService.AccountSnapshot[] snapshots = Array.Empty<AccountMonitoringService.AccountSnapshot>();

        public AccountMonitoringForm(IEnumerable<SteamGuardAccount> accounts)
        {
            this.accounts = (accounts ?? Enumerable.Empty<SteamGuardAccount>()).Where(account => account != null).ToArray();
            Icon = Branding.LoadAppIcon();
            Text = Localizer.Choose("Account monitoring", "Мониторинг аккаунтов");
            ClientSize = new Size(820, 520);
            MinimumSize = new Size(720, 420);
            BackColor = Branding.WindowBackground;
            ForeColor = Branding.HeadingText;
            Font = new Font("Segoe UI", 9F);
            ModernUi.AttachWindowChrome(this, true, false);
            BuildLayout();
            Shown += async (sender, args) => await RefreshAsync(false);
        }

        private void BuildLayout()
        {
            Label intro = new Label
            {
                Text = Localizer.Choose(
                    "VAC, level, games and CS2 inventory are read from Steam Community without an API key. Private profiles may hide some values.",
                    "VAC, уровень, игры и CS2-инвентарь читаются из Steam Community без API-ключа. Приватный профиль может скрывать часть данных."),
                Location = new Point(18, 48),
                Size = new Size(782, 34),
                ForeColor = Branding.MutedText,
                BackColor = Color.Transparent
            };

            btnRefresh.Text = Localizer.Choose("Refresh all", "Обновить все");
            btnRefresh.Location = new Point(680, 90);
            btnRefresh.Size = new Size(120, 34);
            btnRefresh.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            btnRefresh.Click += async (sender, args) => await RefreshAsync(true);
            ModernUi.RoundButton(btnRefresh, true);

            txtSearch.Location = new Point(18, 94);
            txtSearch.Size = new Size(220, 26);
            txtSearch.PlaceholderText = Localizer.Choose("Search account or SteamID", "Поиск аккаунта или SteamID");
            txtSearch.TextChanged += (sender, args) => ApplyView();
            ModernUi.WrapTextBox(txtSearch);

            cmbFilter.Location = new Point(248, 92);
            cmbFilter.Size = new Size(180, 28);
            cmbFilter.DropDownStyle = ComboBoxStyle.DropDownList;
            cmbFilter.Items.AddRange(new object[]
            {
                Localizer.Choose("All accounts", "Все аккаунты"),
                Localizer.Choose("Needs attention", "Требуют внимания"),
                Localizer.Choose("Active sessions", "Активные сессии"),
                Localizer.Choose("Private profiles", "Приватные профили")
            });
            cmbFilter.SelectedIndex = 0;
            cmbFilter.SelectedIndexChanged += (sender, args) => ApplyView();

            cmbSort.Location = new Point(438, 92);
            cmbSort.Size = new Size(180, 28);
            cmbSort.DropDownStyle = ComboBoxStyle.DropDownList;
            cmbSort.Items.AddRange(new object[]
            {
                Localizer.Choose("Sort: Account", "Сортировка: аккаунт"),
                Localizer.Choose("Sort: Level", "Сортировка: уровень"),
                Localizer.Choose("Sort: Inventory", "Сортировка: инвентарь"),
                Localizer.Choose("Sort: VAC first", "Сортировка: VAC сверху")
            });
            cmbSort.SelectedIndex = 0;
            cmbSort.SelectedIndexChanged += (sender, args) => ApplyView();

            grid.Location = new Point(18, 136);
            grid.Size = new Size(782, 340);
            grid.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            grid.AllowUserToAddRows = false;
            grid.AllowUserToDeleteRows = false;
            grid.AllowUserToResizeRows = false;
            grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            grid.BackgroundColor = Branding.CardBackground;
            grid.BorderStyle = BorderStyle.None;
            grid.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
            grid.GridColor = Branding.Outline;
            grid.EnableHeadersVisualStyles = false;
            grid.ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle { BackColor = Branding.AccentDark, ForeColor = Branding.HeadingText };
            grid.DefaultCellStyle = new DataGridViewCellStyle { BackColor = Branding.CardBackground, ForeColor = Branding.HeadingText, SelectionBackColor = Branding.AccentSoft, SelectionForeColor = Color.White };
            grid.RowHeadersVisible = false;
            grid.ReadOnly = true;
            grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            grid.AutoGenerateColumns = false;
            EnsureGridColumns();
            grid.CellDoubleClick += grid_CellDoubleClick;
            lblStatus.Location = new Point(18, 486);
            lblStatus.Size = new Size(782, 22);
            lblStatus.Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            lblStatus.ForeColor = Branding.MutedText;

            Controls.Add(intro);
            Controls.Add(txtSearch);
            Controls.Add(cmbFilter);
            Controls.Add(cmbSort);
            Controls.Add(btnRefresh);
            Controls.Add(grid);
            Controls.Add(lblStatus);
            Paint += ModernUi.PaintGlassBackground;
        }

        private async Task RefreshAsync(bool forceRefresh)
        {
            btnRefresh.Enabled = false;
            grid.Rows.Clear();
            lblStatus.Text = Localizer.Choose("Loading account data...", "Загрузка данных аккаунтов...");
            SemaphoreSlim throttle = new SemaphoreSlim(4);
            try
            {
                Task<AccountMonitoringService.AccountSnapshot>[] tasks = accounts.Select(async account =>
                {
                    await throttle.WaitAsync();
                    try
                    {
                        return await monitoringService.LoadSnapshotAsync(account, forceRefresh);
                    }
                    finally
                    {
                        throttle.Release();
                    }
                }).ToArray();

                snapshots = await Task.WhenAll(tasks);
                ApplyView();
            }
            finally
            {
                throttle.Dispose();
                btnRefresh.Enabled = true;
            }
        }

        private void ApplyView()
        {
            EnsureGridColumns();
            IEnumerable<AccountMonitoringService.AccountSnapshot> view = snapshots;
            string query = txtSearch.Text.Trim();
            if (query.Length > 0)
            {
                view = view.Where(snapshot => snapshot.AccountName.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0
                    || snapshot.SteamId.ToString().IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0);
            }

            string active = Localizer.Choose("Active", "Активна");
            view = cmbFilter.SelectedIndex switch
            {
                1 => view.Where(snapshot => !snapshot.Loaded || snapshot.SessionHealth != active || HasVisibleBan(snapshot.VacStatus)),
                2 => view.Where(snapshot => snapshot.SessionHealth == active),
                3 => view.Where(snapshot => snapshot.IsPrivateProfile),
                _ => view
            };

            view = cmbSort.SelectedIndex switch
            {
                1 => view.OrderByDescending(snapshot => ParseNumber(snapshot.Level)).ThenBy(snapshot => snapshot.AccountName),
                2 => view.OrderByDescending(snapshot => ParseNumber(snapshot.Inventory)).ThenBy(snapshot => snapshot.AccountName),
                3 => view.OrderByDescending(snapshot => HasVisibleBan(snapshot.VacStatus)).ThenBy(snapshot => snapshot.AccountName),
                _ => view.OrderBy(snapshot => snapshot.AccountName)
            };

            AccountMonitoringService.AccountSnapshot[] visible = view.ToArray();
            grid.Rows.Clear();
            foreach (AccountMonitoringService.AccountSnapshot snapshot in visible)
            {
                string profileStatus = snapshot.Loaded
                    ? snapshot.IsPrivateProfile ? Localizer.Choose("Private", "Приватный") : Localizer.Choose("Ready", "Готов")
                    : snapshot.Error;
                int rowIndex = grid.Rows.Add(
                    snapshot.AccountName,
                    snapshot.VacStatus,
                    snapshot.Level,
                    snapshot.Games,
                    snapshot.Inventory,
                    snapshot.SessionHealth,
                    profileStatus);
                DataGridViewRow row = grid.Rows[rowIndex];
                row.Tag = snapshot;
                if (HasVisibleBan(snapshot.VacStatus))
                {
                    row.Cells[1].Style.ForeColor = Branding.Danger;
                }
                if (snapshot.SessionHealth != active)
                {
                    row.Cells[5].Style.ForeColor = Branding.Warning;
                }
                if (!snapshot.Loaded)
                {
                    row.Cells[6].Style.ForeColor = Branding.Danger;
                }
            }

            int cachedCount = snapshots.Count(snapshot => snapshot.IsCached);
            string updated = snapshots.Where(snapshot => snapshot.LoadedAtUtc != default)
                .Select(snapshot => snapshot.LoadedAtUtc.ToLocalTime())
                .DefaultIfEmpty(DateTimeOffset.Now)
                .Max()
                .ToString("g");
            lblStatus.Text = Localizer.Choose(
                $"Shown {visible.Length} of {snapshots.Length}. Updated {updated}. Cache: {cachedCount}. Double-click for inventory.",
                $"Показано {visible.Length} из {snapshots.Length}. Обновлено {updated}. Из кэша: {cachedCount}. Двойной клик — инвентарь.");
        }

        private void EnsureGridColumns()
        {
            if (grid.Columns.Count > 0)
            {
                return;
            }

            grid.Columns.Add("account", Localizer.Choose("Account", "Аккаунт"));
            grid.Columns.Add("vac", "VAC");
            grid.Columns.Add("level", Localizer.Choose("Level", "Уровень"));
            grid.Columns.Add("games", Localizer.Choose("Games", "Игры"));
            grid.Columns.Add("inventory", Localizer.Choose("Tradable CS2", "Торгуемые CS2"));
            grid.Columns.Add("session", Localizer.Choose("Session", "Сессия"));
            grid.Columns.Add("status", Localizer.Choose("Profile", "Профиль"));
        }

        private static bool HasVisibleBan(string value)
        {
            return !string.IsNullOrWhiteSpace(value)
                && value.IndexOf("ban", StringComparison.OrdinalIgnoreCase) >= 0
                && value.IndexOf("No visible", StringComparison.OrdinalIgnoreCase) < 0;
        }

        private static int ParseNumber(string value)
        {
            return int.TryParse(value, out int number) ? number : -1;
        }

        private void grid_CellDoubleClick(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0 || grid.Rows[e.RowIndex].Tag is not AccountMonitoringService.AccountSnapshot snapshot)
            {
                return;
            }
            new InventoryForm(snapshot.Account, monitoringService).Show(this);
        }

        private sealed class InventoryForm : Form
        {
            private readonly SteamGuardAccount account;
            private readonly AccountMonitoringService service;
            private readonly ListView list = new ListView();
            private readonly Label status = new Label();

            public InventoryForm(SteamGuardAccount account, AccountMonitoringService service)
            {
                this.account = account;
                this.service = service;
                Icon = Branding.LoadAppIcon();
                Text = Localizer.Choose("CS2 inventory - ", "CS2-инвентарь - ") + account.AccountName;
                ClientSize = new Size(620, 480);
                BackColor = Branding.WindowBackground;
                ForeColor = Branding.HeadingText;
                ModernUi.AttachWindowChrome(this, true, false);
                list.Location = new Point(16, 48);
                list.Size = new Size(588, 390);
                list.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
                list.View = View.Details;
                list.FullRowSelect = true;
                list.BackColor = Branding.CardBackground;
                list.ForeColor = Branding.HeadingText;
                list.Columns.Add(Localizer.Choose("Item", "Предмет"), 380);
                list.Columns.Add(Localizer.Choose("Amount", "Количество"), 80);
                list.Columns.Add(Localizer.Choose("Flags", "Свойства"), 110);
                status.Location = new Point(16, 446);
                status.Size = new Size(588, 22);
                status.Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
                status.ForeColor = Branding.MutedText;
                Controls.Add(list);
                Controls.Add(status);
                Shown += async (sender, args) => await LoadInventoryAsync();
            }

            private async Task LoadInventoryAsync()
            {
                status.Text = Localizer.Choose("Loading inventory...", "Загрузка инвентаря...");
                AccountMonitoringService.InventoryResult result = await service.LoadInventoryAsync(account);
                if (!result.Success)
                {
                    status.Text = result.Error;
                    return;
                }

                list.BeginUpdate();
                foreach (AccountMonitoringService.InventoryItem item in result.Items.OrderBy(item => item.Name))
                {
                    string flags = string.Join(", ", new[]
                    {
                        item.Tradable ? Localizer.Choose("tradable", "обмен") : null,
                        item.Marketable ? Localizer.Choose("marketable", "маркет") : null
                    }.Where(value => value != null));
                    list.Items.Add(new ListViewItem(new[] { item.Name, item.Amount.ToString(), flags }));
                }
                list.EndUpdate();
                status.Text = Localizer.Choose($"Items: {result.TotalCount}", $"Предметов: {result.TotalCount}");
            }
        }
    }
}
