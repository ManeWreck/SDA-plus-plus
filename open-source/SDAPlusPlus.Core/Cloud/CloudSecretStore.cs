using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;

namespace SDAPlusPlus.Core.Cloud;

internal sealed class CloudSecretStore(string directory)
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("SDA++ Cloud Secret Store");
    private readonly string _path = Path.Combine(directory, "cloud.secret.json");

    public string Load(string key)
    {
        if (!OperatingSystem.IsWindows()) return string.Empty;
        Dictionary<string, string> values = LoadModel();
        if (!values.TryGetValue(key, out string? protectedValue)) return string.Empty;
        byte[]? decrypted = null;
        try
        {
            decrypted = ProtectedData.Unprotect(Convert.FromBase64String(protectedValue), Entropy, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(decrypted);
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException) { return string.Empty; }
        finally { if (decrypted is not null) CryptographicOperations.ZeroMemory(decrypted); }
    }

    public void Save(string key, string value)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("OAuth tokens require Windows DPAPI.");
        Dictionary<string, string> values = LoadModel();
        byte[] raw = Encoding.UTF8.GetBytes(value ?? string.Empty);
        try
        {
            values[key] = Convert.ToBase64String(ProtectedData.Protect(raw, Entropy, DataProtectionScope.CurrentUser));
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            string temporary = _path + ".tmp";
            File.WriteAllText(temporary, JsonConvert.SerializeObject(new SecretModel { Values = values }, Formatting.Indented));
            File.Move(temporary, _path, true);
        }
        finally { CryptographicOperations.ZeroMemory(raw); }
    }

    public byte[] LoadBytes(string key)
    {
        string value = Load(key);
        return string.IsNullOrEmpty(value) ? [] : Convert.FromBase64String(value);
    }

    public void SaveBytes(string key, byte[] value) => Save(key, Convert.ToBase64String(value));

    private Dictionary<string, string> LoadModel()
    {
        if (!File.Exists(_path)) return new(StringComparer.Ordinal);
        try { return JsonConvert.DeserializeObject<SecretModel>(File.ReadAllText(_path))?.Values ?? new(StringComparer.Ordinal); }
        catch (Exception ex) when (ex is IOException or JsonException) { return new(StringComparer.Ordinal); }
    }

    private sealed class SecretModel
    {
        [JsonProperty("values")]
        public Dictionary<string, string> Values { get; set; } = new(StringComparer.Ordinal);
    }
}
