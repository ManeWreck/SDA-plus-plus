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
        private readonly Button restart = new Button();
        private readonly Button cancel = new Button();
        private readonly System.Windows.Forms.Timer countdown = new System.Windows.Forms.Timer { Interval = 1000 };
        private readonly CancellationTokenSource formCancellation = new CancellationTokenSource();
        private CancellationTokenSource attemptCancellation;
        private LocalCloudPairingService service;
        private int attemptId;

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
            description.Text = Localizer.Choose("Scan in SDA++ Mobile. Both devices must share a private network; allow SDA++ through Windows Firewall.", "Сканируйте в SDA++ Mobile. Устройства должны быть в одной частной сети; разрешите SDA++ в брандмауэре Windows.");

            qrCode.SetBounds(55, 116, 280, 280);
            qrCode.SizeMode = PictureBoxSizeMode.Zoom;
            qrCode.BackColor = Color.White;

            status.SetBounds(24, 408, 342, 28);
            status.TextAlign = ContentAlignment.MiddleCenter;
            status.ForeColor = Branding.MutedText;

            restart.SetBounds(40, 447, 145, 34);
            restart.Text = Localizer.Choose("New QR", "Новый QR");
            restart.Click += async (_, __) => await StartPairingAsync();
            ModernUi.RoundButton(restart, true);

            cancel.SetBounds(205, 447, 145, 34);
            cancel.Text = Localizer.Choose("Cancel", "Отмена");
            cancel.Click += (_, __) => Close();
            ModernUi.RoundButton(cancel, false);

            Controls.AddRange(new Control[] { description, qrCode, status, restart, cancel });
            Shown += async (_, __) => await StartPairingAsync();
            FormClosed += (_, __) =>
            {
                formCancellation.Cancel();
                attemptCancellation?.Cancel();
            };
            countdown.Tick += (_, __) => UpdateCountdown();
            Paint += ModernUi.PaintGlassBackground;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                countdown.Dispose();
                formCancellation.Cancel();
                attemptCancellation?.Cancel();
                attemptCancellation?.Dispose();
                formCancellation.Dispose();
                service?.Dispose();
                qrCode.Image?.Dispose();
            }
            base.Dispose(disposing);
        }

        private async Task StartPairingAsync()
        {
            int currentAttempt = ++attemptId;
            countdown.Stop();
            restart.Enabled = false;
            attemptCancellation?.Cancel();
            attemptCancellation?.Dispose();
            service?.Dispose();
            service = null;
            Image previousImage = qrCode.Image;
            qrCode.Image = null;
            previousImage?.Dispose();

            attemptCancellation = CancellationTokenSource.CreateLinkedTokenSource(formCancellation.Token);
            try
            {
                service = new LocalCloudPairingService();
                qrCode.Image = CreateQrBitmap(service.PairingUri, 280, 280);
                countdown.Start();
                UpdateCountdown();
                PairingResult = await service.WaitForResultAsync(attemptCancellation.Token);
                DialogResult = DialogResult.OK;
                Close();
            }
            catch (OperationCanceledException)
            {
                if (!IsDisposed && currentAttempt == attemptId && !formCancellation.IsCancellationRequested)
                {
                    ExpireCurrentQr();
                }
            }
            catch (Exception ex)
            {
                if (!IsDisposed && currentAttempt == attemptId)
                {
                    ExpireCurrentQr();
                    MessageBox.Show(ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
            finally
            {
                if (!IsDisposed && currentAttempt == attemptId)
                {
                    restart.Enabled = true;
                }
            }
        }

        private void ExpireCurrentQr()
        {
            countdown.Stop();
            service?.Dispose();
            service = null;
            Image expiredImage = qrCode.Image;
            qrCode.Image = null;
            expiredImage?.Dispose();
            status.Text = Localizer.Choose("QR expired. Create a new QR.", "Срок QR истёк. Создайте новый QR.");
        }

        private void UpdateCountdown()
        {
            int seconds = service == null ? 0 : Math.Max(0, (int)Math.Ceiling((service.ExpiresUtc - DateTime.UtcNow).TotalSeconds));
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
