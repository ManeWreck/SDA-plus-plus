using System;
using System.Drawing;
using System.Windows.Forms;

namespace Steam_Desktop_Authenticator
{
    internal sealed class CloudPairingHubForm : Form
    {
        public CloudPairingHubForm()
        {
            Text = Localizer.Choose("Connect SDA++ Mobile", "Подключить SDA++ Mobile");
            ClientSize = new Size(410, 254);
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            BackColor = Branding.WindowBackground;
            ForeColor = Branding.HeadingText;
            Icon = Branding.LoadAppIcon();
            ModernUi.AttachWindowChrome(this, false, false);

            Label title = new Label
            {
                Text = Localizer.Choose("Choose transfer direction", "Выберите направление передачи"),
                Bounds = new Rectangle(24, 54, 362, 28),
                Font = new Font("Segoe UI Semibold", 12F, FontStyle.Bold),
                ForeColor = Branding.HeadingText
            };
            Label hint = new Label
            {
                Text = Localizer.Choose(
                    "Only encrypted pairing data is sent. The QR never contains your WebDAV password.",
                    "Передаются только зашифрованные данные. QR никогда не содержит пароль WebDAV."),
                Bounds = new Rectangle(24, 86, 362, 42),
                ForeColor = Branding.MutedText
            };
            Button receive = CreateButton(
                Localizer.Choose("Phone → this PC", "Телефон → этот ПК"), 136, true);
            Button send = CreateButton(
                Localizer.Choose("This PC → phone", "Этот ПК → телефон"), 180, false);

            receive.Click += async (_, __) => await CloudPairingWorkflow.ReceiveFromMobileAsync(this);
            send.Click += (_, __) =>
            {
                try
                {
                    CloudPairingResult settings = CloudPairingWorkflow.LoadDesktopWebDavSettings();
                    using DesktopCloudExportForm export = new DesktopCloudExportForm(settings);
                    export.ShowDialog(this);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            };

            Controls.AddRange(new Control[] { title, hint, receive, send });
            Paint += ModernUi.PaintGlassBackground;
        }

        private Button CreateButton(string text, int y, bool primary)
        {
            Button button = new Button { Text = text, Bounds = new Rectangle(24, y, 362, 34) };
            ModernUi.RoundButton(button, primary);
            return button;
        }
    }
}
