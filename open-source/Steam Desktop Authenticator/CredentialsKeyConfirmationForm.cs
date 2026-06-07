using System;
using System.Drawing;
using System.Windows.Forms;

namespace Steam_Desktop_Authenticator
{
    internal sealed class CredentialsKeyConfirmationForm : Form
    {
        private readonly string generatedKey;
        private readonly TextBox txtGeneratedKey = new TextBox();
        private readonly TextBox txtConfirmKey = new TextBox();
        private readonly Label lblCountdown = new Label();
        private readonly Button btnCopy = new Button();
        private readonly Button btnContinue = new Button();
        private readonly Button btnCancel = new Button();
        private readonly Timer timer = new Timer();
        private int secondsRemaining = 10;

        public CredentialsKeyConfirmationForm(string generatedKey)
        {
            this.generatedKey = generatedKey ?? string.Empty;
            InitializeUi();
        }

        public bool Confirmed { get; private set; }

        private void InitializeUi()
        {
            Icon = Branding.LoadAppIcon();
            Text = Localizer.Choose("Credentials encryption key", "Ключ шифрования логинов");
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ClientSize = new Size(520, 280);
            BackColor = Branding.WindowBackground;
            ForeColor = Branding.HeadingText;

            Label lblTitle = new Label
            {
                AutoSize = false,
                Location = new Point(16, 18),
                Size = new Size(488, 42),
                ForeColor = Branding.HeadingText,
                Font = new Font("Segoe UI Semibold", 10.5F, FontStyle.Bold, GraphicsUnit.Point),
                Text = Localizer.Choose(
                    "Save this generated credentials decryption key now. SDA++ will not let you continue for 10 seconds.",
                    "Сохраните этот сгенерированный ключ расшифровки логинов сейчас. SDA++ не позволит продолжить еще 10 секунд.")
            };

            txtGeneratedKey.Location = new Point(16, 72);
            txtGeneratedKey.Size = new Size(394, 24);
            txtGeneratedKey.ReadOnly = true;
            txtGeneratedKey.Text = generatedKey;

            btnCopy.Location = new Point(420, 70);
            btnCopy.Size = new Size(84, 28);
            btnCopy.Text = Localizer.Choose("Copy key", "Копировать");
            btnCopy.Click += (s, e) =>
            {
                try { Clipboard.SetText(generatedKey); } catch { }
            };

            lblCountdown.Location = new Point(16, 108);
            lblCountdown.Size = new Size(488, 20);
            lblCountdown.ForeColor = Branding.Warning;

            Label lblConfirm = new Label
            {
                AutoSize = false,
                Location = new Point(16, 142),
                Size = new Size(488, 36),
                ForeColor = Branding.MutedText,
                Text = Localizer.Choose(
                    "After the delay, re-enter the key exactly to confirm that you saved it.",
                    "После задержки введите ключ еще раз без ошибок, чтобы подтвердить, что вы его сохранили.")
            };

            txtConfirmKey.Location = new Point(16, 188);
            txtConfirmKey.Size = new Size(488, 24);

            btnContinue.Location = new Point(320, 232);
            btnContinue.Size = new Size(88, 32);
            btnContinue.Text = Localizer.Choose("Continue", "Продолжить");
            btnContinue.Enabled = false;
            btnContinue.Click += btnContinue_Click;

            btnCancel.Location = new Point(416, 232);
            btnCancel.Size = new Size(88, 32);
            btnCancel.Text = Localizer.Choose("Cancel", "Отмена");
            btnCancel.Click += (s, e) => Close();

            Controls.Add(lblTitle);
            Controls.Add(txtGeneratedKey);
            Controls.Add(btnCopy);
            Controls.Add(lblCountdown);
            Controls.Add(lblConfirm);
            Controls.Add(txtConfirmKey);
            Controls.Add(btnContinue);
            Controls.Add(btnCancel);

            ModernUi.WrapTextBox(txtGeneratedKey);
            ModernUi.WrapTextBox(txtConfirmKey);
            ModernUi.RoundButton(btnCopy, false);
            ModernUi.RoundButton(btnContinue, true);
            ModernUi.RoundButton(btnCancel, false);
            ModernUi.AttachWindowChrome(this, false, false);
            Paint += ModernUi.PaintGlassBackground;

            timer.Interval = 1000;
            timer.Tick += timer_Tick;
            UpdateCountdownLabel();
            timer.Start();
        }

        private void timer_Tick(object sender, EventArgs e)
        {
            secondsRemaining--;
            UpdateCountdownLabel();
            if (secondsRemaining <= 0)
            {
                timer.Stop();
                btnContinue.Enabled = true;
                lblCountdown.ForeColor = Branding.Success;
            }
        }

        private void UpdateCountdownLabel()
        {
            lblCountdown.Text = secondsRemaining > 0
                ? Localizer.Choose(
                    $"Continue unlocks in {secondsRemaining} seconds.",
                    $"Кнопка «Продолжить» разблокируется через {secondsRemaining} сек.")
                : Localizer.Choose("You can continue now.", "Теперь можно продолжить.");
        }

        private void btnContinue_Click(object sender, EventArgs e)
        {
            if (!string.Equals(txtConfirmKey.Text.Trim(), generatedKey.Trim(), StringComparison.Ordinal))
            {
                MessageBox.Show(
                    Localizer.Choose("The entered key does not match the generated key.", "Введенный ключ не совпадает со сгенерированным."),
                    Text,
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
                Confirmed = false;
                DialogResult = DialogResult.Cancel;
                Close();
                return;
            }

            DialogResult warning = MessageBox.Show(
                Localizer.Choose(
                    "If you lose this key, your saved login credentials cannot be decrypted or recovered.\n\nContinue?",
                    "Если вы потеряете этот ключ, сохраненные логины нельзя будет расшифровать или восстановить.\n\nПродолжить?"),
                Text,
                MessageBoxButtons.OKCancel,
                MessageBoxIcon.Warning);
            if (warning != DialogResult.OK)
            {
                return;
            }

            Confirmed = true;
            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
