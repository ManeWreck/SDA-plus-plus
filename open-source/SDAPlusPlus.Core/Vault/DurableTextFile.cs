using System.Text;

namespace SDAPlusPlus.Core.Vault;

internal static class DurableTextFile
{
    public static void Write(string path, string contents)
    {
        using var file = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
        byte[] bytes = Encoding.UTF8.GetBytes(contents);
        file.Write(bytes);
        file.Flush(flushToDisk: true);
    }
}
