using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Steam_Desktop_Authenticator
{
    public enum HotkeyOverlayAnchor
    {
        TopCenter,
        TopRight
    }

    public class HotkeyOverlayForm : Form
    {
        private readonly Label titleLabel;
        private readonly Label messageLabel;
        private readonly Timer hideTimer;
        private HotkeyOverlayAnchor anchorMode = HotkeyOverlayAnchor.TopCenter;

        public HotkeyOverlayForm()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            TopMost = true;
            BackColor = Branding.AccentDark;
            ForeColor = Color.White;
            Opacity = 0.98;
            Size = new Size(372, 102);
            Padding = new Padding(0);
            DoubleBuffered = true;

            titleLabel = new Label();
            titleLabel.AutoSize = false;
            titleLabel.Font = new Font("Segoe UI Semibold", 12.5F, FontStyle.Bold, GraphicsUnit.Point);
            titleLabel.ForeColor = Color.White;
            titleLabel.Location = new Point(24, 18);
            titleLabel.Size = new Size(312, 24);

            messageLabel = new Label();
            messageLabel.AutoSize = false;
            messageLabel.Font = new Font("Segoe UI", 9.75F, FontStyle.Regular, GraphicsUnit.Point);
            messageLabel.ForeColor = Color.FromArgb(220, 232, 255);
            messageLabel.Location = new Point(24, 46);
            messageLabel.Size = new Size(324, 32);

            hideTimer = new Timer();
            hideTimer.Interval = 1800;
            hideTimer.Tick += hideTimer_Tick;

            Controls.Add(titleLabel);
            Controls.Add(messageLabel);
        }

        protected override bool ShowWithoutActivation
        {
            get { return true; }
        }

        public void SetAnchorMode(HotkeyOverlayAnchor mode)
        {
            anchorMode = mode;
        }

        public void ShowStatus(string title, string message)
        {
            titleLabel.Text = title;
            messageLabel.Text = message;

            Rectangle area = Screen.PrimaryScreen.WorkingArea;
            if (anchorMode == HotkeyOverlayAnchor.TopRight)
            {
                Location = new Point(area.Right - Width - 28, area.Top + 20);
            }
            else
            {
                Location = new Point(area.Left + ((area.Width - Width) / 2), area.Top + 18);
            }

            ApplyRoundedRegion();

            hideTimer.Stop();
            Show();
            BringToFront();
            Invalidate();
            hideTimer.Start();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            if (Width <= 1 || Height <= 1)
            {
                base.OnPaint(e);
                return;
            }

            Rectangle fillRect = new Rectangle(0, 0, Width - 1, Height - 1);
            using (GraphicsPath path = CreateRoundedRectangle(fillRect, 22))
            using (SolidBrush backgroundBrush = new SolidBrush(Branding.AccentDark))
            using (Pen borderPen = new Pen(Color.FromArgb(118, 168, 255), 2))
            using (SolidBrush accentBrush = new SolidBrush(Branding.Accent))
            {
                e.Graphics.FillPath(backgroundBrush, path);
                e.Graphics.DrawPath(borderPen, path);
                e.Graphics.FillRectangle(accentBrush, new Rectangle(18, 18, 7, Height - 36));
            }

            base.OnPaint(e);
        }

        private void ApplyRoundedRegion()
        {
            if (Width <= 0 || Height <= 0)
            {
                Region = null;
                return;
            }

            using (GraphicsPath path = CreateRoundedRectangle(new Rectangle(0, 0, Width, Height), 22))
            {
                Region = new Region(path);
            }
        }

        private static GraphicsPath CreateRoundedRectangle(Rectangle bounds, int radius)
        {
            int diameter = radius * 2;
            GraphicsPath path = new GraphicsPath();

            path.AddArc(bounds.X, bounds.Y, diameter, diameter, 180, 90);
            path.AddArc(bounds.Right - diameter, bounds.Y, diameter, diameter, 270, 90);
            path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
            path.AddArc(bounds.X, bounds.Bottom - diameter, diameter, diameter, 90, 90);
            path.CloseFigure();
            return path;
        }

        private void hideTimer_Tick(object sender, EventArgs e)
        {
            hideTimer.Stop();
            Hide();
        }
    }
}
