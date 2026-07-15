using System;
using System.Drawing;
using System.Security.Cryptography;
using System.Windows.Forms;

namespace Steam_Desktop_Authenticator
{
    internal static class DesktopAdminStepUp
    {
        private const int Iterations = 210000;

        public static bool Require(IWin32Window owner, DesktopRegistrySettings settings)
        {
            if (settings == null) return false;
            long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            if (settings.AdminPinLockedUntil > now)
            {
                long seconds = Math.Max(1, (settings.AdminPinLockedUntil.Value - now + 999) / 1000);
                MessageBox.Show(
                    Localizer.Choose($"Too many attempts. Try again in {seconds} seconds.", $"Слишком много попыток. Повторите через {seconds} сек."),
                    Localizer.Choose("Admin PIN", "Admin PIN"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }
            bool configuring = string.IsNullOrWhiteSpace(settings.AdminPinSalt) ||
                string.IsNullOrWhiteSpace(settings.AdminPinHash) || settings.AdminPinIterations <= 0;
            using AdminPinForm form = new AdminPinForm(configuring);
            if (form.ShowDialog(owner) != DialogResult.OK) return false;

            char[] pin = form.Pin.ToCharArray();
            char[] confirmation = form.Confirmation.ToCharArray();
            try
            {
                if (configuring)
                {
                    if (pin.Length < 6 || pin.Length > 12 || !AllDigits(pin) || !pin.AsSpan().SequenceEqual(confirmation))
                    {
                        MessageBox.Show(
                            Localizer.Choose("Use 6-12 digits and enter the same PIN twice.", "Используйте 6-12 цифр и введите одинаковый PIN дважды."),
                            form.Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        return false;
                    }
                    byte[] salt = RandomNumberGenerator.GetBytes(16);
                    byte[] hash = Derive(pin, salt, Iterations);
                    try
                    {
                        settings.AdminPinSalt = Convert.ToBase64String(salt);
                        settings.AdminPinHash = Convert.ToBase64String(hash);
                        settings.AdminPinIterations = Iterations;
                        settings.AdminPinFailedAttempts = 0;
                        settings.AdminPinLockedUntil = null;
                        DesktopRegistrySettingsStore.Save(settings);
                        return true;
                    }
                    finally
                    {
                        CryptographicOperations.ZeroMemory(salt);
                        CryptographicOperations.ZeroMemory(hash);
                    }
                }

                byte[] storedSalt = Convert.FromBase64String(settings.AdminPinSalt);
                byte[] storedHash = Convert.FromBase64String(settings.AdminPinHash);
                byte[] candidate = Derive(pin, storedSalt, settings.AdminPinIterations);
                try
                {
                    bool valid = CryptographicOperations.FixedTimeEquals(candidate, storedHash);
                    if (valid)
                    {
                        settings.AdminPinFailedAttempts = 0;
                        settings.AdminPinLockedUntil = null;
                        DesktopRegistrySettingsStore.Save(settings);
                    }
                    else
                    {
                        settings.AdminPinFailedAttempts++;
                        if (settings.AdminPinFailedAttempts >= 5)
                        {
                            settings.AdminPinFailedAttempts = 0;
                            settings.AdminPinLockedUntil = DateTimeOffset.UtcNow.AddSeconds(30).ToUnixTimeMilliseconds();
                        }
                        DesktopRegistrySettingsStore.Save(settings);
                        MessageBox.Show(Localizer.Choose("Wrong Admin PIN.", "Неверный Admin PIN."), form.Text,
                            MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                    return valid;
                }
                finally
                {
                    CryptographicOperations.ZeroMemory(storedSalt);
                    CryptographicOperations.ZeroMemory(storedHash);
                    CryptographicOperations.ZeroMemory(candidate);
                }
            }
            catch (FormatException)
            {
                MessageBox.Show(Localizer.Choose("Admin PIN data is damaged.", "Данные Admin PIN повреждены."), form.Text,
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }
            finally
            {
                Array.Clear(pin, 0, pin.Length);
                Array.Clear(confirmation, 0, confirmation.Length);
                form.ClearSensitiveFields();
            }
        }

        private static byte[] Derive(char[] pin, byte[] salt, int iterations)
        {
            byte[] bytes = System.Text.Encoding.UTF8.GetBytes(pin);
            try
            {
                return Rfc2898DeriveBytes.Pbkdf2(bytes, salt, iterations, HashAlgorithmName.SHA256, 32);
            }
            finally { CryptographicOperations.ZeroMemory(bytes); }
        }

        private static bool AllDigits(char[] value)
        {
            foreach (char character in value)
                if (character < '0' || character > '9') return false;
            return true;
        }
    }

    internal sealed class AdminPinForm : Form
    {
        private readonly TextBox pin = new TextBox();
        private readonly TextBox confirmation = new TextBox();

        public string Pin => pin.Text;
        public string Confirmation => confirmation.Text;

        public AdminPinForm(bool configuring)
        {
            Text = configuring
                ? Localizer.Choose("Create Admin PIN", "Создать Admin PIN")
                : Localizer.Choose("Confirm Admin action", "Подтвердить Admin-действие");
            ClientSize = new Size(360, configuring ? 190 : 140);
            StartPosition = FormStartPosition.CenterParent;
            BackColor = Branding.WindowBackground;
            ForeColor = Branding.HeadingText;
            Icon = Branding.LoadAppIcon();
            ModernUi.AttachWindowChrome(this, false, false);

            Label explanation = new Label
            {
                Text = configuring
                    ? Localizer.Choose("Create a 6-12 digit PIN for device administration.", "Создайте PIN из 6-12 цифр для управления устройствами.")
                    : Localizer.Choose("Enter the Admin PIN to continue.", "Введите Admin PIN, чтобы продолжить."),
                Bounds = new Rectangle(20, 46, 320, 28),
                ForeColor = Branding.MutedText
            };
            pin.SetBounds(20, 78, 320, 28);
            pin.UseSystemPasswordChar = true;
            pin.MaxLength = 12;
            if (configuring)
            {
                confirmation.SetBounds(20, 112, 320, 28);
                confirmation.UseSystemPasswordChar = true;
                confirmation.MaxLength = 12;
            }
            Button confirm = new Button
            {
                Text = Localizer.Choose("Confirm", "Подтвердить"),
                Bounds = new Rectangle(220, configuring ? 148 : 104, 120, 32),
                DialogResult = DialogResult.OK
            };
            ModernUi.RoundButton(confirm, true);
            AcceptButton = confirm;
            Controls.Add(explanation);
            Controls.Add(pin);
            if (configuring) Controls.Add(confirmation);
            Controls.Add(confirm);
            Paint += ModernUi.PaintGlassBackground;
        }

        public void ClearSensitiveFields()
        {
            pin.Clear();
            confirmation.Clear();
        }
    }
}
