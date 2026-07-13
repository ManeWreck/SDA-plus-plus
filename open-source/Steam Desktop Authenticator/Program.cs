using System;
using System.Windows.Forms;
using System.Diagnostics;
using CommandLine;

namespace Steam_Desktop_Authenticator
{
    static class Program
    {
        public static Process PriorProcess()
        // Returns a System.Diagnostics.Process pointing to
        // a pre-existing process with the same name as the
        // current one, if any; or null if the current process
        // is unique.
        {
            try
            {
                Process curr = Process.GetCurrentProcess();
                Process[] procs = Process.GetProcessesByName(curr.ProcessName);
                foreach (Process p in procs)
                {
                    if ((p.Id != curr.Id) &&
                        (p.MainModule.FileName == curr.MainModule.FileName))
                        return p;
                }
                return null;
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>
        /// The main entry point for the application.
        /// </summary>
        [STAThread]
        static void Main(string[] args)
        {
            // run the program only once
            if (PriorProcess() != null)
            {
                MessageBox.Show("Другой экземпляр " + Branding.AppName + " уже запущен.", Branding.FullAppName);
                return;
            }

            // Parse command line arguments
            CommandLineOptions options = new();
            Parser.Default.ParseArguments<CommandLineOptions>(args)
                .WithParsed(o => options = o);

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            Manifest man;

            try
            {
                man = Manifest.GetManifest();
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
                    Process.Start(@"https://github.com/Jessecar96/SteamDesktopAuthenticator/wiki/Help!-I'm-locked-out-of-my-account");
                    return;
                }
            }

            Localizer.SetLanguage(man.UiLanguage);

            using (var notice = new StartupNoticeForm(
                Localizer.Choose(
                    Branding.FullAppName + " includes faster QR approvals, session tools, and hotkeys.\n\nIt is not affiliated with Valve or Steam. Keep backups of your maFiles and use it only on systems you trust.",
                    Branding.FullAppName + " включает быстрые QR-подтверждения, инструменты управления сессиями и хоткеи.\n\nПриложение не связано с Valve или Steam. Храните резервные копии maFiles и используйте его только на доверенных системах.")))
            {
                notice.ShowDialog();
            }

            if (man.FirstRun)
            {
                if (man.Entries.Count > 0)
                {
                    // Already has accounts, just run
                    MainForm mf = new MainForm();
                    mf.SetEncryptionKey(options.EncryptionKey);
                    mf.StartSilent(options.Silent);
                    Application.Run(mf);
                }
                else
                {
                    // No accounts, run welcome form
                    Application.Run(new WelcomeForm());
                }
            }
            else
            {
                MainForm mf = new MainForm();
                mf.SetEncryptionKey(options.EncryptionKey);
                mf.StartSilent(options.Silent);
                Application.Run(mf);
            }
        }
    }
}
