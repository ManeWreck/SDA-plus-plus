using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace Steam_Desktop_Authenticator
{
    internal sealed class CredentialsManagerForm : Form
    {
        private readonly StoredCredentialLoginService loginService = new StoredCredentialLoginService();
        private readonly StoredCredentialsVault vault;
        private readonly Manifest manifest;
        private readonly ListBox listEntries = new ListBox();
        private readonly TextBox txtAccount = new TextBox();
        private readonly TextBox txtUsername = new TextBox();
        private readonly TextBox txtPassword = new TextBox();
        private readonly TextBox txtStoragePath = new TextBox();
        private readonly Label lblStatus = new Label();
        private readonly CheckBox chkEncrypted = new CheckBox();
        private readonly Button btnSaveEntry = new Button();
        private readonly Button btnRemoveEntry = new Button();
        private readonly Button btnImport = new Button();
        private readonly Button btnBrowse = new Button();
        private readonly Button btnClose = new Button();

        public CredentialsManagerForm()
        {
            manifest = Manifest.GetManifest(true);
            vault = loginService.GetVault();
            InitializeUi();
            ReloadEntries();
        }

        private void InitializeUi()
        {
            Icon = Branding.LoadAppIcon();
            Text = Localizer.Choose("Manage login credentials", "Управление логинами");
            StartPosition = FormStartPosition.CenterParent;
            MinimumSize = new Size(720, 520);
            ClientSize = new Size(720, 520);
            BackColor = Branding.WindowBackground;
            ForeColor = Branding.HeadingText;
            ModernUi.AttachWindowChrome(this, false, false);
            Paint += ModernUi.PaintGlassBackground;

            Label lblPath = new Label { Text = Localizer.Choose("Credentials file:", "Файл логинов:"), Location = new Point(16, 48), Size = new Size(140, 22), ForeColor = Branding.MutedText };
            txtStoragePath.Location = new Point(16, 72);
            txtStoragePath.Size = new Size(574, 24);
            txtStoragePath.Text = manifest.CredentialsStoragePath;
            btnBrowse.Location = new Point(600, 70);
            btnBrowse.Size = new Size(96, 28);
            btnBrowse.Text = Localizer.Choose("Choose file", "Выбрать");
            btnBrowse.Click += btnBrowse_Click;

            chkEncrypted.Location = new Point(16, 108);
            chkEncrypted.Size = new Size(320, 22);
            chkEncrypted.Text = Localizer.Choose("Encrypt credentials file", "Шифровать файл логинов");
            chkEncrypted.Checked = manifest.CredentialsRequireEncryption;

            lblStatus.Location = new Point(16, 136);
            lblStatus.Size = new Size(680, 36);
            lblStatus.ForeColor = Branding.MutedText;

            listEntries.Location = new Point(16, 182);
            listEntries.Size = new Size(260, 264);
            listEntries.SelectedIndexChanged += listEntries_SelectedIndexChanged;

            int rightX = 292;
            Controls.Add(new Label { Text = Localizer.Choose("Account name", "Имя аккаунта"), Location = new Point(rightX, 182), Size = new Size(160, 18), ForeColor = Branding.MutedText });
            txtAccount.Location = new Point(rightX, 204);
            txtAccount.Size = new Size(404, 24);

            Controls.Add(new Label { Text = Localizer.Choose("Login", "Логин"), Location = new Point(rightX, 240), Size = new Size(160, 18), ForeColor = Branding.MutedText });
            txtUsername.Location = new Point(rightX, 262);
            txtUsername.Size = new Size(404, 24);

            Controls.Add(new Label { Text = Localizer.Choose("Password", "Пароль"), Location = new Point(rightX, 298), Size = new Size(160, 18), ForeColor = Branding.MutedText });
            txtPassword.Location = new Point(rightX, 320);
            txtPassword.Size = new Size(404, 24);
            txtPassword.UseSystemPasswordChar = true;

            btnSaveEntry.Location = new Point(rightX, 360);
            btnSaveEntry.Size = new Size(126, 32);
            btnSaveEntry.Text = Localizer.Choose("Save credentials", "Сохранить");
            btnSaveEntry.Click += btnSaveEntry_Click;

            btnRemoveEntry.Location = new Point(rightX + 136, 360);
            btnRemoveEntry.Size = new Size(126, 32);
            btnRemoveEntry.Text = Localizer.Choose("Remove", "Удалить");
            btnRemoveEntry.Click += btnRemoveEntry_Click;

            btnImport.Location = new Point(rightX + 272, 360);
            btnImport.Size = new Size(126, 32);
            btnImport.Text = Localizer.Choose("Import file", "Импорт файла");
            btnImport.Click += btnImport_Click;

            btnClose.Location = new Point(570, 456);
            btnClose.Size = new Size(126, 34);
            btnClose.Text = Localizer.Choose("Close", "Закрыть");
            btnClose.Click += (s, e) => Close();

            Controls.Add(lblPath);
            Controls.Add(txtStoragePath);
            Controls.Add(btnBrowse);
            Controls.Add(chkEncrypted);
            Controls.Add(lblStatus);
            Controls.Add(listEntries);
            Controls.Add(txtAccount);
            Controls.Add(txtUsername);
            Controls.Add(txtPassword);
            Controls.Add(btnSaveEntry);
            Controls.Add(btnRemoveEntry);
            Controls.Add(btnImport);
            Controls.Add(btnClose);

            ModernUi.WrapTextBox(txtStoragePath);
            ModernUi.WrapTextBox(txtAccount);
            ModernUi.WrapTextBox(txtUsername);
            ModernUi.WrapTextBox(txtPassword);
            ModernUi.RoundButton(btnBrowse, false);
            ModernUi.RoundButton(btnSaveEntry, true);
            ModernUi.RoundButton(btnRemoveEntry, false);
            ModernUi.RoundButton(btnImport, false);
            ModernUi.RoundButton(btnClose, false);
        }

        private void btnBrowse_Click(object sender, EventArgs e)
        {
            using SaveFileDialog dialog = new SaveFileDialog
            {
                Filter = Localizer.Choose("Credentials files|*.json|All files|*.*", "Файлы логинов|*.json|Все файлы|*.*"),
                FileName = Path.GetFileName(vault.ResolveVaultPath()),
                InitialDirectory = Path.GetDirectoryName(vault.ResolveVaultPath())
            };

            if (dialog.ShowDialog(this) == DialogResult.OK)
            {
                txtStoragePath.Text = dialog.FileName;
            }
        }

        private void btnImport_Click(object sender, EventArgs e)
        {
            using OpenFileDialog dialog = new OpenFileDialog
            {
                Filter = Localizer.Choose("Text files|*.txt;*.list|All files|*.*", "Текстовые файлы|*.txt;*.list|Все файлы|*.*")
            };
            if (dialog.ShowDialog(this) != DialogResult.OK)
            {
                return;
            }

            List<StoredCredentialsVault.StoredCredential> imported = vault.ParseCredentialPairs(dialog.FileName);
            if (imported.Count == 0)
            {
                MessageBox.Show(Localizer.Choose("No valid login:password pairs were found.", "Не найдено валидных пар login:password."), Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            List<StoredCredentialsVault.StoredCredential> existing = loginService.GetAllCredentials();
            foreach (StoredCredentialsVault.StoredCredential item in imported)
            {
                StoredCredentialsVault.StoredCredential found = existing.FirstOrDefault(entry =>
                    string.Equals(entry.AccountName, item.AccountName, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(entry.Username, item.Username, StringComparison.OrdinalIgnoreCase));
                if (found == null)
                {
                    existing.Add(item);
                }
                else
                {
                    found.Username = item.Username;
                    found.Password = item.Password;
                    found.UpdatedUtc = DateTime.UtcNow;
                }
            }

            SaveAll(existing);
            ReloadEntries();
            lblStatus.Text = Localizer.Choose("Credentials imported.", "Логины импортированы.");
        }

        private void btnSaveEntry_Click(object sender, EventArgs e)
        {
            if (string.IsNullOrWhiteSpace(txtAccount.Text) || string.IsNullOrWhiteSpace(txtUsername.Text) || string.IsNullOrWhiteSpace(txtPassword.Text))
            {
                MessageBox.Show(Localizer.Choose("Account name, login and password are required.", "Нужны имя аккаунта, логин и пароль."), Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            List<StoredCredentialsVault.StoredCredential> entries = loginService.GetAllCredentials();
            StoredCredentialsVault.StoredCredential existing = entries.FirstOrDefault(item =>
                string.Equals(item.AccountName, txtAccount.Text.Trim(), StringComparison.OrdinalIgnoreCase)
                || string.Equals(item.Username, txtUsername.Text.Trim(), StringComparison.OrdinalIgnoreCase));
            if (existing == null)
            {
                existing = new StoredCredentialsVault.StoredCredential();
                entries.Add(existing);
            }

            existing.AccountName = txtAccount.Text.Trim();
            existing.Username = txtUsername.Text.Trim();
            existing.Password = txtPassword.Text;
            existing.UpdatedUtc = DateTime.UtcNow;
            SaveAll(entries);
            ReloadEntries();
            SelectEntry(txtAccount.Text.Trim());
            lblStatus.Text = Localizer.Choose("Credentials saved.", "Логины сохранены.");
        }

        private void btnRemoveEntry_Click(object sender, EventArgs e)
        {
            if (listEntries.SelectedItem is not StoredCredentialsVault.StoredCredential selected)
            {
                return;
            }

            loginService.RemoveCredentials(selected.SteamId, selected.AccountName);
            ReloadEntries();
            ClearEditor();
            lblStatus.Text = Localizer.Choose("Credentials removed.", "Логины удалены.");
        }

        private void listEntries_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (listEntries.SelectedItem is not StoredCredentialsVault.StoredCredential selected)
            {
                return;
            }

            txtAccount.Text = selected.AccountName;
            txtUsername.Text = selected.Username;
            txtPassword.Text = selected.Password;
        }

        private void ReloadEntries()
        {
            listEntries.DisplayMember = nameof(StoredCredentialsVault.StoredCredential.AccountName);
            listEntries.Items.Clear();
            foreach (StoredCredentialsVault.StoredCredential item in loginService.GetAllCredentials().OrderBy(item => item.AccountName, StringComparer.OrdinalIgnoreCase))
            {
                listEntries.Items.Add(item);
            }

            StoredCredentialsVault.VaultStatus status = loginService.GetVaultStatus();
            lblStatus.Text = BuildStatusText(status);
        }

        private string BuildStatusText(StoredCredentialsVault.VaultStatus status)
        {
            List<string> parts = new List<string>();
            parts.Add(status.FileExists
                ? Localizer.Choose("Credentials found", "Логины найдены")
                : Localizer.Choose("Credentials file not created yet", "Файл логинов еще не создан"));
            parts.Add(status.Encrypted
                ? Localizer.Choose("Credentials encrypted", "Логины зашифрованы")
                : Localizer.Choose("Credentials stored without encryption", "Логины хранятся без шифрования"));
            if (manifest.CredentialsCloudEnabled)
            {
                parts.Add(Localizer.Choose("Credentials stored in cloud", "Логины синхронизируются в облако"));
            }
            if (status.Encrypted && !status.DecryptionKeyAvailable)
            {
                parts.Add(Localizer.Choose("Wrong decryption key or key missing", "Неверный ключ расшифровки или ключ отсутствует"));
            }

            return string.Join(" • ", parts);
        }

        private void ApplyStorageSettingsIfNeeded()
        {
            bool encrypted = chkEncrypted.Checked;
            string configuredPath = txtStoragePath.Text.Trim();
            if (string.IsNullOrWhiteSpace(configuredPath))
            {
                throw new InvalidOperationException(Localizer.Choose("Choose where credentials are stored first.", "Сначала выберите, где хранить логины."));
            }

            string key = null;
            if (encrypted)
            {
                StoredCredentialsVault.VaultStatus status = loginService.GetVaultStatus();
                if (!status.Encrypted || !status.DecryptionKeyAvailable || !string.Equals(Path.GetFullPath(status.Path), Path.GetFullPath(Path.IsPathRooted(configuredPath) ? configuredPath : Path.Combine(Manifest.GetExecutableDir(), configuredPath)), StringComparison.OrdinalIgnoreCase))
                {
                    key = vault.GenerateEncryptionKey();
                    using CredentialsKeyConfirmationForm keyForm = new CredentialsKeyConfirmationForm(key);
                    if (keyForm.ShowDialog(this) != DialogResult.OK || !keyForm.Confirmed)
                    {
                        throw new InvalidOperationException(Localizer.Choose("Credentials encryption setup was canceled.", "Настройка шифрования логинов была отменена."));
                    }
                }
                else
                {
                    key = vault.TryGetRememberedKey(status.Path);
                }
            }
            else
            {
                DialogResult warning = MessageBox.Show(
                    Localizer.Choose(
                        "Saving login credentials without encryption is dangerous because anyone with access to this file can read your passwords.\n\nContinue?",
                        "Сохранять логины без шифрования опасно: любой, у кого есть доступ к файлу, сможет прочитать ваши пароли.\n\nПродолжить?"),
                    Text,
                    MessageBoxButtons.OKCancel,
                    MessageBoxIcon.Warning);
                if (warning != DialogResult.OK)
                {
                    throw new InvalidOperationException(Localizer.Choose("Unencrypted credentials storage was canceled.", "Сохранение логинов без шифрования отменено."));
                }
            }

            vault.ConfigureStorage(configuredPath, encrypted, key, encrypted);
            manifest.AllowUnencryptedLocalCredentials = !encrypted;
            manifest.CredentialsRequireEncryption = encrypted;
            manifest.CredentialsStoragePath = configuredPath;
            manifest.Save();
        }

        private void SaveAll(List<StoredCredentialsVault.StoredCredential> entries)
        {
            ApplyStorageSettingsIfNeeded();
            vault.ReplaceAllCredentials(entries);
        }

        private void SelectEntry(string accountName)
        {
            for (int i = 0; i < listEntries.Items.Count; i++)
            {
                if (listEntries.Items[i] is StoredCredentialsVault.StoredCredential item
                    && string.Equals(item.AccountName, accountName, StringComparison.OrdinalIgnoreCase))
                {
                    listEntries.SelectedIndex = i;
                    return;
                }
            }
        }

        private void ClearEditor()
        {
            txtAccount.Clear();
            txtUsername.Clear();
            txtPassword.Clear();
        }
    }
}
