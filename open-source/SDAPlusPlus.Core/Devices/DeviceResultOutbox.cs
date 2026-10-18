using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace SDAPlusPlus.Core.Devices;

public sealed record QueuedDeviceResult(DeviceExecutionReceipt Receipt, string SignedResult);

// One instance owns this directory. Keep it alongside the replay journal, outside wipeable vault data.
// Successful HTTP delivery does not remove a result: the relay is not proof of Admin receipt.
public sealed class DeviceResultOutbox(string directory, IDeviceRequestSigner recipient, TimeProvider? clock = null)
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("SDA++ result outbox v1");
    private readonly TimeProvider time = clock ?? TimeProvider.System;

    public async Task RecoverAsync(IDeviceCommandJournal journal, string vaultId, string adminKey, CancellationToken ct = default)
    {
        var receipts = await journal.ReadRecoverableAsync(ct);
        if (receipts.Any(r => r.Command.VaultId != vaultId || r.Command.DeviceId != recipient.DeviceId))
            throw new InvalidDataException("Journal belongs to another identity.");
        foreach (var receipt in receipts)
            await EnqueueAsync(receipt, adminKey, ct);
    }

    public async Task<QueuedDeviceResult> EnqueueAsync(DeviceExecutionReceipt receipt, string adminKey, CancellationToken ct = default)
    {
        await gate.WaitAsync(ct);
        try
        {
            string path = PathFor(receipt.Command);
            if (File.Exists(path))
            {
                var previous = Read(path, adminKey);
                if (previous.Receipt != receipt) throw new InvalidDataException("Conflicting final result.");
                return previous;
            }
            var queued = new QueuedDeviceResult(receipt, SignedDeviceReceipt.Create(receipt,
                receipt.Command.VaultId, adminKey, recipient, time.GetUtcNow().ToUnixTimeMilliseconds()));
            Directory.CreateDirectory(directory);
            string temporary = path + ".tmp-" + Guid.NewGuid().ToString("N");
            byte[] plain = JsonSerializer.SerializeToUtf8Bytes(queued);
            try
            {
                if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("DPAPI required.");
                byte[] encrypted = ProtectedData.Protect(plain, Entropy, DataProtectionScope.CurrentUser);
                using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                { stream.Write(encrypted); stream.Flush(true); }
                File.Move(temporary, path);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(plain);
                if (File.Exists(temporary)) File.Delete(temporary);
            }
            return queued;
        }
        finally { gate.Release(); }
    }

    // No execution callback exists in this path. It works independently of command polling/expiry.
    public async Task<int> RetryAsync(string vaultId, string adminKey,
        Func<QueuedDeviceResult, CancellationToken, Task<bool>> send, CancellationToken ct = default)
    {
        await gate.WaitAsync(ct);
        try
        {
            if (!Directory.Exists(directory)) return 0;
            // Validate the complete batch before sending, including corrupted records.
            var pending = Directory.EnumerateFiles(directory, "*.ack").Select(p => Read(p, adminKey)).ToArray();
            int delivered = 0;
            foreach (var item in pending)
            {
                ct.ThrowIfCancellationRequested();
                if (item.Receipt.Command.VaultId != vaultId) throw new InvalidDataException("Wrong outbox vault.");
            }
            foreach (var item in pending)
                if (await send(item, ct)) delivered++;
            return delivered;
        }
        finally { gate.Release(); }
    }

    private QueuedDeviceResult Read(string path, string adminKey)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("DPAPI required.");
        if (new FileInfo(path).Length > 2_000_000) throw new InvalidDataException("Oversized outbox entry.");
        byte[] plain = ProtectedData.Unprotect(File.ReadAllBytes(path), Entropy, DataProtectionScope.CurrentUser);
        try
        {
            var item = JsonSerializer.Deserialize<QueuedDeviceResult>(plain) ?? throw new InvalidDataException("Empty outbox entry.");
            var entry = item.Receipt.Command;
            if (Path.GetFullPath(path) != Path.GetFullPath(PathFor(entry))) throw new InvalidDataException("Outbox identity mismatch.");
            var command = EndToEndTrust.Verify<DeviceCommandV2>(entry.SignedEnvelope!, adminKey);
            var result = EndToEndTrust.ValidateResult(item.SignedResult, recipient.PublicKey, command,
                entry.SignedEnvelope!, time.GetUtcNow().ToUnixTimeMilliseconds());
            if (entry.DeviceId != recipient.DeviceId || entry.VaultId != command.VaultId || entry.CommandId != command.CommandId ||
                entry.Type != command.CommandType || entry.ExpiresAt != command.ExpiresAt || result.Result != item.Receipt.State.ToString())
                throw new InvalidDataException("Outbox receipt mismatch.");
            return item;
        }
        finally { CryptographicOperations.ZeroMemory(plain); }
    }
    private string PathFor(IncomingDeviceCommand command) => Path.Combine(directory,
        Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(
            new[] { command.VaultId, command.DeviceId, command.CommandId }))) + ".ack");
}
