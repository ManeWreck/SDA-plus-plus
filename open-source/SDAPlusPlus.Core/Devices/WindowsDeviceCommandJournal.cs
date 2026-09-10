using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace SDAPlusPlus.Core.Devices;

// Keep outside data erased by WipeLocalData. One processor must own this directory.
public sealed class WindowsDeviceCommandJournal(string directory) : IDeviceCommandJournal
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("SDA++ command journal v1");

    public Task<DeviceExecutionReceipt?> ReadAsync(string vaultId, string deviceId, string commandId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string path = ReceiptPath(vaultId, deviceId, commandId);
        byte[] encrypted;
        try { encrypted = File.ReadAllBytes(path); }
        catch (FileNotFoundException) { return Task.FromResult<DeviceExecutionReceipt?>(null); }
        catch (DirectoryNotFoundException) { return Task.FromResult<DeviceExecutionReceipt?>(null); }
        byte[] plain = Unprotect(encrypted);
        try
        {
            var receipt = JsonSerializer.Deserialize<DeviceExecutionReceipt>(plain)
                ?? throw new InvalidDataException("Empty command receipt.");
            if (receipt.Command is null || receipt.Command.VaultId != vaultId || receipt.Command.DeviceId != deviceId ||
                receipt.Command.CommandId != commandId || !Enum.IsDefined(receipt.State))
                throw new InvalidDataException("Invalid command receipt.");
            return Task.FromResult<DeviceExecutionReceipt?>(receipt);
        }
        finally { CryptographicOperations.ZeroMemory(plain); }
    }

    public async Task SaveAsync(DeviceExecutionReceipt receipt, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        // Never silently overwrite an unreadable or mismatched record.
        var existing = await ReadAsync(receipt.Command.VaultId, receipt.Command.DeviceId, receipt.Command.CommandId, cancellationToken);
        if (existing is not null && existing.Command != receipt.Command)
            throw new InvalidDataException("Conflicting command receipt.");
        string path = ReceiptPath(receipt.Command.VaultId, receipt.Command.DeviceId, receipt.Command.CommandId);
        Directory.CreateDirectory(directory);
        string temporary = path + ".tmp-" + Guid.NewGuid().ToString("N");
        byte[] plain = JsonSerializer.SerializeToUtf8Bytes(receipt);
        try
        {
            if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Windows DPAPI required.");
            byte[] encrypted = ProtectedData.Protect(plain, Entropy, DataProtectionScope.CurrentUser);
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                stream.Write(encrypted);
                stream.Flush(flushToDisk: true);
            }
            if (existing is null) File.Move(temporary, path);
            else File.Replace(temporary, path, null);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plain);
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    private string ReceiptPath(string vault, string device, string command) => Path.Combine(directory,
        Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new[] { vault, device, command }))) + ".receipt");

    private static byte[] Unprotect(byte[] encrypted)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Windows DPAPI required.");
        return ProtectedData.Unprotect(encrypted, Entropy, DataProtectionScope.CurrentUser);
    }
}
