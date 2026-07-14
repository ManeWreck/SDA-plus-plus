using System;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Steam_Desktop_Authenticator
{
    internal static class CloudPairingWorkflow
    {
        public static async Task<bool> ReceiveFromMobileAsync(IWin32Window owner)
        {
            using CloudPairingForm pairing = new CloudPairingForm();
            if (pairing.ShowDialog(owner) != DialogResult.OK || pairing.PairingResult == null)
                return false;

            CloudPairingResult result = pairing.PairingResult;
            try
            {
                Manifest manifest = Manifest.GetManifest(true);
                SaveWebDavSettings(manifest, result);

                var provider = new WebDavStorageProvider(result.Url, result.Login, result.Password, result.RemotePath);
                await provider.TestConnectionAsync();
                DialogResult pull = MessageBox.Show(
                    Localizer.Choose(
                        "SDA++ Mobile connected securely. Pull the encrypted vault from WebDAV now?",
                        "SDA++ Mobile безопасно подключён. Загрузить зашифрованное хранилище из WebDAV сейчас?"),
                    Branding.FullAppName, MessageBoxButtons.YesNo, MessageBoxIcon.Information);
                if (pull == DialogResult.Yes)
                {
                    CloudSyncService service = new CloudSyncService();
                    CloudSyncService.CloudPullPlan plan = await service.PreparePullAsync(provider, false);
                    DialogResult confirm = MessageBox.Show(
                        Localizer.Choose(
                            $"Cloud contains {plan.RemoteAccountCount} account(s). Continue with the safe restore?",
                            $"В облаке найдено аккаунтов: {plan.RemoteAccountCount}. Продолжить безопасное восстановление?"),
                        Branding.FullAppName, MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                    if (confirm == DialogResult.Yes)
                    {
                        service.CommitPull(plan);
                        manifest = Manifest.GetManifest(true);
                        SaveWebDavSettings(manifest, result);
                    }
                }
                return true;
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    Localizer.Choose(
                        "Cloud settings were saved, but the connection or restore failed:\n",
                        "Настройки облака сохранены, но подключение или восстановление не удалось:\n") + ex.Message,
                    Branding.FullAppName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }
            finally
            {
                result.Password = string.Empty;
            }
        }

        public static CloudPairingResult LoadDesktopWebDavSettings()
        {
            Manifest manifest = Manifest.GetManifest(true);
            if (manifest.CloudProvider != CloudProvider.WebDav)
                throw new InvalidOperationException(Localizer.Choose(
                    "Select WebDAV as the cloud provider in Settings first.",
                    "Сначала выберите WebDAV как облачный провайдер в настройках."));

            string password = new CloudSecretStore().Load("webdav-password");
            if (!Uri.TryCreate(manifest.WebDavUrl, UriKind.Absolute, out Uri uri) || uri.Scheme != Uri.UriSchemeHttps ||
                string.IsNullOrWhiteSpace(manifest.WebDavUsername) || string.IsNullOrEmpty(password))
            {
                throw new InvalidOperationException(Localizer.Choose(
                    "Save a complete HTTPS WebDAV profile in Settings first.",
                    "Сначала сохраните полный профиль HTTPS WebDAV в настройках."));
            }

            return new CloudPairingResult
            {
                Url = manifest.WebDavUrl.Trim(),
                Login = manifest.WebDavUsername.Trim(),
                Password = password,
                RemotePath = string.IsNullOrWhiteSpace(manifest.WebDavRemotePath) ? "SDAppVault" : manifest.WebDavRemotePath.Trim()
            };
        }

        private static void SaveWebDavSettings(Manifest manifest, CloudPairingResult result)
        {
            manifest.CloudProvider = CloudProvider.WebDav;
            manifest.WebDavUrl = result.Url;
            manifest.WebDavUsername = result.Login;
            manifest.WebDavRemotePath = result.RemotePath;
            manifest.FirstRun = false;
            new CloudSecretStore().Save("webdav-password", result.Password);
            manifest.Save();
        }
    }
}
