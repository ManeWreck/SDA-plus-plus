using SteamAuth;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Steam_Desktop_Authenticator
{
    public partial class ConfirmationFormWeb : Form
    {
        private readonly IReadOnlyList<SteamGuardAccount> steamAccounts;

        public ConfirmationFormWeb(SteamGuardAccount steamAccount)
            : this(new[] { steamAccount })
        {
        }

        public ConfirmationFormWeb(IEnumerable<SteamGuardAccount> accounts)
        {
            InitializeComponent();
            Icon = Branding.LoadAppIcon();
            steamAccounts = (accounts ?? Enumerable.Empty<SteamGuardAccount>())
                .Where(account => account != null)
                .ToArray();
            ModernUi.AttachWindowChrome(this, true, false);
            ModernUi.ShiftControlsDown(this, ModernUi.HeaderHeight + 8);
            ApplyTheme();
            Text = Localizer.Choose("Confirmations - all accounts", "Подтверждения - все аккаунты");
        }

        private void ApplyTheme()
        {
            BackColor = Branding.WindowBackground;
            ForeColor = Branding.HeadingText;
            splitContainer1.BackColor = Branding.WindowBackground;
            splitContainer1.Panel1.BackColor = Color.Transparent;
            splitContainer1.Panel2.BackColor = Branding.WindowBackground;
            splitContainer1.SplitterDistance = 42;
            splitContainer1.Panel2.AutoScroll = true;
            btnRefresh.Height = 34;
            ModernUi.RoundButton(btnRefresh, true);
            Paint += ModernUi.PaintGlassBackground;
        }

        private async Task LoadData()
        {
            splitContainer1.Panel2.Controls.Clear();
            List<(SteamGuardAccount Account, Confirmation Confirmation)> items = new();
            List<string> failures = new();

            foreach (SteamGuardAccount account in steamAccounts)
            {
                if (account.Session == null || account.Session.IsRefreshTokenExpired())
                {
                    failures.Add(account.AccountName + ": " + Localizer.Choose("session expired", "сессия истекла"));
                    continue;
                }

                try
                {
                    Confirmation[] confirmations = await account.FetchConfirmationsAsync();
                    if (confirmations != null)
                    {
                        items.AddRange(confirmations.Select(confirmation => (account, confirmation)));
                    }
                }
                catch (Exception ex)
                {
                    failures.Add(account.AccountName + ": " + ex.Message);
                }
            }

            if (items.Count == 0)
            {
                string message = Localizer.Choose(
                    "Nothing to confirm or deny.",
                    "Нет подтверждений для принятия или отклонения.");
                if (failures.Count > 0)
                {
                    message += "\n\n" + string.Join("\n", failures);
                }

                Label emptyLabel = new Label
                {
                    Text = message,
                    AutoSize = true,
                    MaximumSize = new Size(Math.Max(220, splitContainer1.Panel2.ClientSize.Width - 48), 0),
                    ForeColor = failures.Count > 0 ? Branding.Danger : Branding.MutedText,
                    Location = new Point(24, 20),
                    BackColor = Color.Transparent
                };
                splitContainer1.Panel2.Controls.Add(emptyLabel);
                return;
            }

            foreach ((SteamGuardAccount Account, Confirmation Confirmation) item in items.AsEnumerable().Reverse())
            {
                AddConfirmationCard(item.Account, item.Confirmation);
            }
        }

        private void AddConfirmationCard(SteamGuardAccount account, Confirmation confirmation)
        {
            Panel panel = new Panel
            {
                Dock = DockStyle.Top,
                Height = 154,
                Padding = new Padding(0, 0, 0, 12),
                BackColor = Color.Transparent
            };
            panel.Paint += (s, e) => PaintCard(panel, e);

            if (!string.IsNullOrEmpty(confirmation.Icon))
            {
                PictureBox pictureBox = new PictureBox
                {
                    Width = 60,
                    Height = 60,
                    Location = new Point(20, 34),
                    SizeMode = PictureBoxSizeMode.Zoom
                };
                try
                {
                    pictureBox.Load(confirmation.Icon);
                }
                catch (Exception ex)
                {
                    Console.WriteLine("Failed to load confirmation icon: " + ex.Message);
                }
                panel.Controls.Add(pictureBox);
            }

            panel.Controls.Add(new Label
            {
                Text = account.AccountName,
                AutoSize = true,
                ForeColor = Branding.Accent,
                Font = new Font("Segoe UI Semibold", 8.5F),
                Location = new Point(90, 14),
                BackColor = Color.Transparent
            });

            panel.Controls.Add(new Label
            {
                Text = $"{confirmation.Headline}\n{confirmation.Creator}",
                AutoSize = true,
                ForeColor = Branding.HeadingText,
                Location = new Point(90, 36),
                BackColor = Color.Transparent
            });

            ConfirmationButton acceptButton = new ConfirmationButton
            {
                Text = confirmation.Accept,
                Location = new Point(90, 70),
                FlatStyle = FlatStyle.Flat,
                BackColor = Branding.Accent,
                ForeColor = Color.White,
                Confirmation = confirmation,
                Account = account
            };
            ModernUi.RoundButton(acceptButton, true);
            acceptButton.Click += btnAccept_Click;
            panel.Controls.Add(acceptButton);

            ConfirmationButton cancelButton = new ConfirmationButton
            {
                Text = confirmation.Cancel,
                Location = new Point(180, 70),
                FlatStyle = FlatStyle.Flat,
                BackColor = Branding.AccentSoft,
                ForeColor = Branding.HeadingText,
                Confirmation = confirmation,
                Account = account
            };
            ModernUi.RoundButton(cancelButton, false);
            cancelButton.Click += btnCancel_Click;
            panel.Controls.Add(cancelButton);

            panel.Controls.Add(new Label
            {
                Text = string.Join("\n", confirmation.Summary),
                AutoSize = true,
                ForeColor = Branding.MutedText,
                Location = new Point(90, 104),
                BackColor = Color.Transparent
            });

            splitContainer1.Panel2.Controls.Add(panel);
        }

        private static void PaintCard(Panel panel, PaintEventArgs e)
        {
            if (panel.Width <= 1 || panel.Height <= 11)
            {
                return;
            }

            Rectangle bounds = new Rectangle(0, 0, panel.Width - 1, panel.Height - 10);
            using GraphicsPath path = new GraphicsPath();
            path.AddArc(bounds.X, bounds.Y, 18, 18, 180, 90);
            path.AddArc(bounds.Right - 18, bounds.Y, 18, 18, 270, 90);
            path.AddArc(bounds.Right - 18, bounds.Bottom - 18, 18, 18, 0, 90);
            path.AddArc(bounds.X, bounds.Bottom - 18, 18, 18, 90, 90);
            path.CloseFigure();
            using SolidBrush brush = new SolidBrush(Color.FromArgb(214, Branding.CardBackground));
            using Pen borderPen = new Pen(Color.FromArgb(120, Branding.Outline));
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.FillPath(brush, path);
            e.Graphics.DrawPath(borderPen, path);
        }

        private async void btnAccept_Click(object sender, EventArgs e)
        {
            ConfirmationButton button = (ConfirmationButton)sender;
            await button.Account.AcceptConfirmation(button.Confirmation);
            await LoadData();
        }

        private async void btnCancel_Click(object sender, EventArgs e)
        {
            ConfirmationButton button = (ConfirmationButton)sender;
            await button.Account.DenyConfirmation(button.Confirmation);
            await LoadData();
        }

        private async void btnRefresh_Click(object sender, EventArgs e)
        {
            btnRefresh.Enabled = false;
            btnRefresh.Text = Localizer.Choose("Refreshing...", "Обновление...");
            await LoadData();
            btnRefresh.Enabled = true;
            btnRefresh.Text = Localizer.Choose("Refresh", "Обновить");
        }

        private async void ConfirmationFormWeb_Shown(object sender, EventArgs e)
        {
            btnRefresh.Enabled = false;
            btnRefresh.Text = Localizer.Choose("Refreshing...", "Обновление...");
            await LoadData();
            btnRefresh.Enabled = true;
            btnRefresh.Text = Localizer.Choose("Refresh", "Обновить");
        }
    }
}
