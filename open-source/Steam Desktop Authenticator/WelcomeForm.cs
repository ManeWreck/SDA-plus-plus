using System;
using System.IO;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Steam_Desktop_Authenticator
{
    public partial class WelcomeForm : Form
    {
        private Manifest man;

        public WelcomeForm()
        {
            InitializeComponent();
            Icon = Branding.LoadAppIcon();
            ModernUi.AttachWindowChrome(this, false, false);
            ModernUi.ShiftControlsDown(this, ModernUi.HeaderHeight + 8);
            ApplyTheme();
            ApplyLocalization();
            man = Manifest.GetManifest();
        }

        private void ApplyTheme()
        {
            Text = Branding.FullAppName;
            BackColor = Branding.WindowBackground;
            ForeColor = Branding.HeadingText;

            foreach (Button button in Controls.OfType<Button>())
            {
                ModernUi.RoundButton(button, false);
                button.Font = new Font("Segoe UI Semibold", 8.75F, FontStyle.Regular, GraphicsUnit.Point);
            }

            btnJustStart.BackColor = Branding.Accent;
            btnJustStart.ForeColor = Color.White;
            btnJustStart.FlatAppearance.BorderColor = Branding.Accent;
            ModernUi.RoundButton(btnJustStart, true);
            label1.ForeColor = Branding.HeadingText;
            label2.ForeColor = Branding.MutedText;
            Paint += ModernUi.PaintGlassBackground;
        }

        private void ApplyLocalization()
        {
            Text = Branding.FullAppName;
            label1.Text = Localizer.Choose("Welcome to\r\nSDA++", "Добро пожаловать в\r\nSDA++");
            label2.Text = Localizer.Choose("Choose how you want to get started:", "Выберите, как начать работу:");
            btnImportConfig.Text = Localizer.Choose("Import an existing SDA / SDA++ setup from another folder on this PC.", "Импортировать существующую настройку SDA / SDA++\r\nиз другой папки на этом ПК.");
            btnJustStart.Text = Localizer.Choose("Start fresh with QR-focused tools\r\nand sign into my Steam account(s).", "Начать с нуля с упором на QR-инструменты\r\nи войти в мои аккаунты Steam.");
        }

        private void btnJustStart_Click(object sender, EventArgs e)
        {
            // Mark as not first run anymore
            man.FirstRun = false;
            man.Save();

            showMainForm();
        }

        private void btnImportConfig_Click(object sender, EventArgs e)
        {
            // Let the user select the config dir
            FolderBrowserDialog folderBrowser = new FolderBrowserDialog();
            folderBrowser.Description = "Выберите папку со старой установкой SDA или SDA++";
            DialogResult userClickedOK = folderBrowser.ShowDialog();

            if (userClickedOK == DialogResult.OK)
            {
                string path = folderBrowser.SelectedPath;
                string pathToCopy = null;

                if (Directory.Exists(path + "/maFiles"))
                {
                    // User selected the root install dir
                    pathToCopy = path + "/maFiles";
                }
                else if (File.Exists(path + "/manifest.json"))
                {
                    // User selected the maFiles dir
                    pathToCopy = path;
                }
                else
                {
                    // Could not find either.
                    MessageBox.Show("В этой папке нет manifest.json или папки maFiles.\nПожалуйста, выберите место, где была установлена SDA или SDA++.", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }

                // Copy the contents of the config dir to the new config dir
                string currentPath = Manifest.GetExecutableDir();

                // Create config dir if we don't have it
                if (!Directory.Exists(currentPath + "/maFiles"))
                {
                    Directory.CreateDirectory(currentPath + "/maFiles");
                }

                // Copy all files from the old dir to the new one
                foreach (string newPath in Directory.GetFiles(pathToCopy, "*.*", SearchOption.AllDirectories))
                {
                    File.Copy(newPath, newPath.Replace(pathToCopy, currentPath + "/maFiles"), true);
                }

                // Set first run in manifest
                try
                {
                    man = Manifest.GetManifest(true);
                    man.FirstRun = false;
                    man.Save();
                }
                catch (ManifestParseException)
                {
                    // Manifest file was corrupted, generate a new one.
                    try
                    {
                        MessageBox.Show("Настройки были повреждены и были сброшены к значениям по умолчанию.", Branding.FullAppName, MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
                        man = Manifest.GenerateNewManifest(true);
                    }
                    catch (MaFileEncryptedException)
                    {
                        // An maFile was encrypted, we're fucked.
                        MessageBox.Show("SDA++ не смог восстановить ваши аккаунты, потому что в прошлой установке использовалось шифрование.\nВам придется восстановить доступ к аккаунтам Steam через удаление аутентификатора.\nНажмите OK, чтобы открыть инструкцию.", Branding.FullAppName, MessageBoxButtons.OK, MessageBoxIcon.Error);
                        System.Diagnostics.Process.Start(@"https://github.com/Jessecar96/SteamDesktopAuthenticator/wiki/Help!-I'm-locked-out-of-my-account");
                        this.Close();
                        return;
                    }
                }

                // All done!
                MessageBox.Show("Все аккаунты и настройки были импортированы. Нажмите OK, чтобы продолжить.", "Импорт аккаунтов", MessageBoxButtons.OK, MessageBoxIcon.Information);
                showMainForm();
            }

        }

        private void showMainForm()
        {
            this.Hide();
            new MainForm().Show();
        }
    }
}
