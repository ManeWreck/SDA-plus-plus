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
    internal sealed class CloudPairingForm : Form
    {
        private readonly PictureBox qrCode = new PictureBox();
        private readonly Label description = new Label();
        private readonly Label status = new Label();
        private readonly Button cancel = new Button();
        private readonly System.Windows.Forms.Timer countdown = new System.Windows.Forms.Timer { Interval = 1000 };
        private readonly CancellationTokenSource cancellation = new CancellationTokenSource();
        private LocalCloudPairingService service;

        public CloudPairingResult PairingResult { get; private set; }

        public CloudPairingForm()
        {
            Text = Localizer.Choose("Connect SDA++ Mobile", "Подключить SDA++ Mobile");
            ClientSize = new Size(390, 500);
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            BackColor = Branding.WindowBackground;
            ForeColor = Branding.HeadingText;
            Icon = Branding.LoadAppIcon();
            ModernUi.AttachWindowChrome(this, false, false);

            description.SetBounds(24, 54, 342, 54);
            description.TextAlign = ContentAlignment.MiddleCenter;
            description.ForeColor = Branding.MutedText;
            description.Text = Localizer.Choose("Open QR in SDA++ Mobile and scan this code. Both devices must be on the same private network.", "Откройте QR в SDA++ Mobile и отсканируйте код. Оба устройства должны быть в одной частной сети.");

            qrCode.SetBounds(55, 116, 280, 280);
            qrCode.SizeMode = PictureBoxSizeMode.Zoom;
            qrCode.BackColor = Color.White;

            status.SetBounds(24, 408, 342, 28);
            status.TextAlign = ContentAlignment.MiddleCenter;
            status.ForeColor = Branding.MutedText;

            cancel.SetBounds(120, 447, 150, 34);
            cancel.Text = Localizer.Choose("Cancel", "Отмена");
            cancel.Click += (_, __) => Close();
            ModernUi.RoundButton(cancel, false);

            Controls.AddRange(new Control[] { description, qrCode, status, cancel });
            Shown += async (_, __) => await StartPairingAsync();
            FormClosed += (_, __) => cancellation.Cancel();
            countdown.Tick += (_, __) => UpdateCountdown();
            Paint += ModernUi.PaintGlassBackground;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                countdown.Dispose();
                cancellation.Cancel();
                cancellation.Dispose();
                service?.Dispose();
                qrCode.Image?.Dispose();
            }
            base.Dispose(disposing);
        }

        private async Task StartPairingAsync()
        {
            try
            {
                service = new LocalCloudPairingService();
                qrCode.Image = CreateQrBitmap(service.PairingUri, 280, 280);
                countdown.Start();
                UpdateCountdown();
                PairingResult = await service.WaitForResultAsync(cancellation.Token);
                DialogResult = DialogResult.OK;
                Close();
            }
            catch (OperationCanceledException)
            {
                if (!IsDisposed) status.Text = Localizer.Choose("Pairing expired.", "Время сопряжения истекло.");
            }
            catch (Exception ex)
            {
                if (!IsDisposed) MessageBox.Show(ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void UpdateCountdown()
        {
            int seconds = service == null ? 120 : Math.Max(0, (int)Math.Ceiling((service.ExpiresUtc - DateTime.UtcNow).TotalSeconds));
            status.Text = Localizer.Choose($"Waiting for phone... {seconds}s", $"Ожидание телефона... {seconds} сек.");
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
