using System;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;
using SteamAuth;
using System.Drawing.Drawing2D;

namespace Steam_Desktop_Authenticator
{
    public partial class ConfirmationFormWeb : Form
    {
        private SteamGuardAccount steamAccount;

        public ConfirmationFormWeb(SteamGuardAccount steamAccount)
        {
            InitializeComponent();
            Icon = Branding.LoadAppIcon();
            this.steamAccount = steamAccount;
            ModernUi.AttachWindowChrome(this, true, false);
            ModernUi.ShiftControlsDown(this, ModernUi.HeaderHeight + 8);
            ApplyTheme();
            this.Text = String.Format("Подтверждения обменов - {0}", steamAccount.AccountName);
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
            this.splitContainer1.Panel2.Controls.Clear();

            // Check for a valid refresh token first
            if (steamAccount.Session.IsRefreshTokenExpired())
            {
                MessageBox.Show("Сессия истекла. Используйте кнопку «Войти заново» в меню выбранного аккаунта.", "Подтверждения обменов", MessageBoxButtons.OK, MessageBoxIcon.Error);
                this.Close();
                return;
            }

            // Check for a valid access token, refresh it if needed
            if (steamAccount.Session.IsAccessTokenExpired())
            {
                try
                {
                    await steamAccount.Session.RefreshAccessToken();
                }
                catch (Exception ex)
                {
                    MessageBox.Show(ex.Message, "Ошибка входа в Steam", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    this.Close();
                    return;
                }
            }

            try
            {
                var confirmations = await steamAccount.FetchConfirmationsAsync();

                if (confirmations == null || confirmations.Length == 0)
                {
                    Label errorLabel = new Label() { Text = "Нечего подтверждать или отклонять", AutoSize = true, ForeColor = Branding.MutedText, Location = new Point(24, 20), BackColor = Color.Transparent };
                    this.splitContainer1.Panel2.Controls.Add(errorLabel);
                }

                foreach (var confirmation in confirmations)
                {
                    Panel panel = new Panel() { Dock = DockStyle.Top, Height = 132, Padding = new Padding(0, 0, 0, 12), BackColor = Color.Transparent };
                    panel.Paint += (s, e) =>
                    {
                        Rectangle bounds = new Rectangle(0, 0, panel.Width - 1, panel.Height - 10);
                        using (GraphicsPath path = new GraphicsPath())
                        {
                            path.AddArc(bounds.X, bounds.Y, 18, 18, 180, 90);
                            path.AddArc(bounds.Right - 18, bounds.Y, 18, 18, 270, 90);
                            path.AddArc(bounds.Right - 18, bounds.Bottom - 18, 18, 18, 0, 90);
                            path.AddArc(bounds.X, bounds.Bottom - 18, 18, 18, 90, 90);
                            path.CloseFigure();
                            using (SolidBrush brush = new SolidBrush(Color.FromArgb(214, Branding.CardBackground)))
                            using (Pen borderPen = new Pen(Color.FromArgb(120, Branding.Outline)))
                            {
                                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                                e.Graphics.FillPath(brush, path);
                                e.Graphics.DrawPath(borderPen, path);
                            }
                        }
                    };

                    if (!string.IsNullOrEmpty(confirmation.Icon))
                    {
                        PictureBox pictureBox = new PictureBox() { Width = 60, Height = 60, Location = new Point(20, 20), SizeMode = PictureBoxSizeMode.Zoom };
                        try
                        {
                            pictureBox.Load(confirmation.Icon);
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine("Failed to load avatar: " + ex.Message);
                        }
                        panel.Controls.Add(pictureBox);
                    }

                    Label nameLabel = new Label()
                    {
                        Text = $"{confirmation.Headline}\n{confirmation.Creator}",
                        AutoSize = true,
                        ForeColor = Branding.HeadingText,
                        Location = new Point(90, 20),
                        BackColor = Color.Transparent
                    };
                    panel.Controls.Add(nameLabel);

                    ConfirmationButton acceptButton = new ConfirmationButton()
                    {
                        Text = confirmation.Accept,
                        Location = new Point(90, 50),
                        FlatStyle = FlatStyle.Flat,
                        BackColor = Branding.Accent,
                        ForeColor = Color.White,
                        Confirmation = confirmation
                    };
                    ModernUi.RoundButton(acceptButton, true);
                    acceptButton.Click += btnAccept_Click;
                    panel.Controls.Add(acceptButton);

                    ConfirmationButton cancelButton = new ConfirmationButton()
                    {
                        Text = confirmation.Cancel,
                        Location = new Point(180, 50),
                        FlatStyle = FlatStyle.Flat,
                        BackColor = Branding.AccentSoft,
                        ForeColor = Branding.HeadingText,
                        Confirmation = confirmation
                    };
                    ModernUi.RoundButton(cancelButton, false);
                    cancelButton.Click += btnCancel_Click;
                    panel.Controls.Add(cancelButton);

                    Label summaryLabel = new Label()
                    {
                        Text = String.Join("\n", confirmation.Summary),
                        AutoSize = true,
                        ForeColor = Branding.MutedText,
                        Location = new Point(90, 80),
                        BackColor = Color.Transparent
                    };
                    panel.Controls.Add(summaryLabel);

                    this.splitContainer1.Panel2.Controls.Add(panel);
                }
            }
            catch (Exception ex)
            {
                Label errorLabel = new Label() { Text = "Что-то пошло не так:\n" + ex.Message, AutoSize = true, ForeColor = Branding.Danger, Location = new Point(20, 20), BackColor = Color.Transparent };
                this.splitContainer1.Panel2.Controls.Add(errorLabel);
            }
        }

        private async void btnAccept_Click(object sender, EventArgs e)
        {
            var button = (ConfirmationButton)sender;
            var confirmation = button.Confirmation;
            bool result = await steamAccount.AcceptConfirmation(confirmation);

            await this.LoadData();
        }

        private async void btnCancel_Click(object sender, EventArgs e)
        {
            var button = (ConfirmationButton)sender;
            var confirmation = button.Confirmation;
            bool result = await steamAccount.DenyConfirmation(confirmation);

            await this.LoadData();
        }


        private async void btnRefresh_Click(object sender, EventArgs e)
        {
            this.btnRefresh.Enabled = false;
            this.btnRefresh.Text = "Обновление...";

            await this.LoadData();

            this.btnRefresh.Enabled = true;
            this.btnRefresh.Text = "Обновить";
        }

        private async void ConfirmationFormWeb_Shown(object sender, EventArgs e)
        {
            this.btnRefresh.Enabled = false;
            this.btnRefresh.Text = "Обновление...";

            await this.LoadData();

            this.btnRefresh.Enabled = true;
            this.btnRefresh.Text = "Обновить";
        }
    }
}
