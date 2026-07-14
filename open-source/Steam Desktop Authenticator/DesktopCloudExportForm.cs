using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using ZXing;
using ZXing.Common;
using ZXing.Rendering;

namespace Steam_Desktop_Authenticator
{
    internal sealed class DesktopCloudExportForm : Form
    {
        private readonly CloudPairingResult settings;
        private readonly PictureBox qr = new PictureBox();
        private readonly Label code = new Label();
        private readonly Label status = new Label();
        private readonly CancellationTokenSource cancellation = new CancellationTokenSource();
        private DesktopCloudExportPairingService service;

        public DesktopCloudExportForm(CloudPairingResult settings)
        {
            this.settings = settings;
            Text = Localizer.Choose("Send cloud settings to phone", "Передать настройки облака на телефон");
            ClientSize = new Size(390, 520);
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            BackColor = Branding.WindowBackground;
            ForeColor = Branding.HeadingText;
            Icon = Branding.LoadAppIcon();
            ModernUi.AttachWindowChrome(this, false, false);

            Label hint = new Label
            {
                Text = Localizer.Choose("Scan this QR on the phone, then enter the one-time code shown below.", "Отсканируйте QR на телефоне, затем введите показанный ниже одноразовый код."),
                Bounds = new Rectangle(24, 52, 342, 44), TextAlign = ContentAlignment.MiddleCenter, ForeColor = Branding.MutedText
            };
            qr.SetBounds(55, 102, 280, 280);
            qr.SizeMode = PictureBoxSizeMode.Zoom;
            qr.BackColor = Color.White;
            code.SetBounds(24, 392, 342, 40);
            code.TextAlign = ContentAlignment.MiddleCenter;
            code.Font = new Font("Consolas", 18F, FontStyle.Bold);
            code.ForeColor = Branding.Accent;
            status.SetBounds(24, 438, 342, 30);
            status.TextAlign = ContentAlignment.MiddleCenter;
            status.ForeColor = Branding.MutedText;
            Button close = new Button { Text = Localizer.Choose("Close", "Закрыть"), Bounds = new Rectangle(120, 474, 150, 32) };
            ModernUi.RoundButton(close, false);
            close.Click += (_, __) => Close();
            Controls.AddRange(new Control[] { hint, qr, code, status, close });
            Shown += async (_, __) => await StartAsync();
            FormClosed += (_, __) => cancellation.Cancel();
            Paint += ModernUi.PaintGlassBackground;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                cancellation.Cancel();
                cancellation.Dispose();
                service?.Dispose();
                qr.Image?.Dispose();
            }
            base.Dispose(disposing);
        }

        private async Task StartAsync()
        {
            try
            {
                service = new DesktopCloudExportPairingService(settings);
                qr.Image = CreateQrBitmap(service.PairingUri, 280, 280);
                code.Text = service.VerificationCode;
                status.Text = Localizer.Choose("Waiting for phone...", "Ожидание телефона...");
                await service.WaitForDeliveryAsync(cancellation.Token);
                status.Text = Localizer.Choose("Encrypted settings delivered.", "Зашифрованные настройки переданы.");
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                status.Text = Localizer.Choose("Transfer failed.", "Ошибка передачи.");
                MessageBox.Show(ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private static Bitmap CreateQrBitmap(string content, int width, int height)
        {
            var writer = new BarcodeWriterPixelData
            {
                Format = BarcodeFormat.QR_CODE,
                Options = new EncodingOptions { Width = width, Height = height, Margin = 2, PureBarcode = true }
            };
            PixelData pixels = writer.Write(content);
            var bitmap = new Bitmap(pixels.Width, pixels.Height, PixelFormat.Format32bppRgb);
            BitmapData data = bitmap.LockBits(new Rectangle(0, 0, pixels.Width, pixels.Height), ImageLockMode.WriteOnly, PixelFormat.Format32bppRgb);
            try { Marshal.Copy(pixels.Pixels, 0, data.Scan0, pixels.Pixels.Length); }
            finally { bitmap.UnlockBits(data); }
            return bitmap;
        }
    }
}
