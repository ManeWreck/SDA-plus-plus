using Newtonsoft.Json;
using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace Steam_Desktop_Authenticator
{
    internal sealed class WebDavSecretStore
    {
        private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("SDA++ by Manewreck WebDAV Secret Store");

        private sealed class SecretModel
        {
            [JsonProperty("password")]
            public string ProtectedPassword { get; set; }
        }

        private static string GetFilePath()
        {
            return Path.Combine(Manifest.GetExecutableDir(), "maFiles", "webdav.secret.json");
        }

        public string LoadPassword()
        {
            string path = GetFilePath();
            if (!File.Exists(path))
            {
                return string.Empty;
            }

            try
            {
                SecretModel model = JsonConvert.DeserializeObject<SecretModel>(File.ReadAllText(path));
                if (string.IsNullOrWhiteSpace(model?.ProtectedPassword))
                {
                    return string.Empty;
                }

                byte[] encrypted = Convert.FromBase64String(model.ProtectedPassword);
                byte[] decrypted = ProtectedData.Unprotect(encrypted, Entropy, DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(decrypted);
            }
            catch
            {
                return string.Empty;
            }
        }

        public void SavePassword(string password)
        {
            string directory = Path.GetDirectoryName(GetFilePath());
            if (!Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            byte[] raw = Encoding.UTF8.GetBytes(password ?? string.Empty);
            byte[] encrypted = ProtectedData.Protect(raw, Entropy, DataProtectionScope.CurrentUser);
            SecretModel model = new SecretModel
            {
                ProtectedPassword = Convert.ToBase64String(encrypted)
            };

            File.WriteAllText(GetFilePath(), JsonConvert.SerializeObject(model, Formatting.Indented));
        }
    }
}
