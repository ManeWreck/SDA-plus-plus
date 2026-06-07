using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace Steam_Desktop_Authenticator
{
    internal sealed class CloudSecretStore
    {
        private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("SDA++ Cloud Secret Store");

        private sealed class SecretModel
        {
            [JsonProperty("values")]
            public Dictionary<string, string> Values { get; set; } = new Dictionary<string, string>();
        }

        private static string GetFilePath()
        {
            return Path.Combine(Manifest.GetExecutableDir(), "maFiles", "cloud.secret.json");
        }

        public string Load(string key)
        {
            SecretModel model = LoadModel();
            if (!model.Values.TryGetValue(key, out string protectedValue) || string.IsNullOrWhiteSpace(protectedValue))
            {
                return string.Empty;
            }

            try
            {
                byte[] encrypted = Convert.FromBase64String(protectedValue);
                byte[] decrypted = ProtectedData.Unprotect(encrypted, Entropy, DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(decrypted);
            }
            catch
            {
                return string.Empty;
            }
        }

        public void Save(string key, string value)
        {
            SecretModel model = LoadModel();
            byte[] raw = Encoding.UTF8.GetBytes(value ?? string.Empty);
            byte[] encrypted = ProtectedData.Protect(raw, Entropy, DataProtectionScope.CurrentUser);
            model.Values[key] = Convert.ToBase64String(encrypted);

            string path = GetFilePath();
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, JsonConvert.SerializeObject(model, Formatting.Indented));
        }

        public byte[] LoadBytes(string key)
        {
            string value = Load(key);
            return string.IsNullOrEmpty(value) ? Array.Empty<byte>() : Convert.FromBase64String(value);
        }

        public void SaveBytes(string key, byte[] value)
        {
            Save(key, Convert.ToBase64String(value ?? Array.Empty<byte>()));
        }

        private static SecretModel LoadModel()
        {
            string path = GetFilePath();
            if (!File.Exists(path))
            {
                return new SecretModel();
            }

            try
            {
                return JsonConvert.DeserializeObject<SecretModel>(File.ReadAllText(path)) ?? new SecretModel();
            }
            catch
            {
                return new SecretModel();
            }
        }
    }
}
