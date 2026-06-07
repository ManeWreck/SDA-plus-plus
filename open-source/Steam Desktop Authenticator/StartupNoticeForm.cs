using System;
using System.Drawing;
using System.Windows.Forms;

namespace Steam_Desktop_Authenticator
{
    internal sealed class StartupNoticeForm : Form
    {
        public StartupNoticeForm(string message)
        {
            Text = Branding.FullAppName;
            Icon = Branding.LoadAppIcon();
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = true;
            BackColor = Branding.WindowBackground;
            ForeColor = Branding.HeadingText;
            ClientSize = new Size(500, 190);
            Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point);

            var iconBox = new PictureBox
            {
                Location = new Point(22, 28),
                Size = new Size(48, 48),
                BackColor = Color.Transparent,
                SizeMode = PictureBoxSizeMode.CenterImage,
                Image = SystemIcons.Information.ToBitmap()
            };

            var lblMessage = new Label
            {
                Location = new Point(84, 24),
                Size = new Size(388, 96),
                ForeColor = Branding.HeadingText,
                BackColor = Color.Transparent,
                Text = message
            };

            var btnOk = new Button
            {
                Text = "OK",
                Size = new Size(108, 34),
                Location = new Point(364, 136)
            };
            btnOk.Click += (_, __) => DialogResult = DialogResult.OK;
            ModernUi.RoundButton(btnOk, true);

            Controls.Add(iconBox);
            Controls.Add(lblMessage);
            Controls.Add(btnOk);

            Paint += ModernUi.PaintGlassBackground;
            AcceptButton = btnOk;
        }
    }
}
