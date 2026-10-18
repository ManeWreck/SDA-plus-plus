using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace SDAPlusPlus.Core.Devices;

public sealed record AdminResultRecord(string FirstSignedResult, string? ConflictingSignedResult = null);
public sealed record AdminResultAcceptance(DeviceResultV2 FirstResult, bool Duplicate, bool RequiresReconciliation);

// Single owner per directory; all evidence is reverified against the local command archive on read.
public sealed class AdminResultInbox(string directory, TimeProvider? clock = null)
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly TimeProvider time = clock ?? TimeProvider.System;
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("SDA++ Admin result inbox v1");

    public async Task<AdminResultAcceptance?> ReadAsync(IssuedDeviceCommand issued, string adminKey, string vaultId, CancellationToken ct = default)
    {
        await gate.WaitAsync(ct);
        try
        {
            var command = EndToEndTrust.Verify<DeviceCommandV2>(issued.Envelope, adminKey);
            if (command.VaultId != vaultId) throw new CryptographicException("Wrong command vault.");
            string path = PathFor(vaultId, command.CommandId);
            if (!File.Exists(path)) return null;
            var record = Read(path);
            long now = time.GetUtcNow().ToUnixTimeMilliseconds();
            var first = AdminCommandResultVerifier.Verify(issued, record.FirstSignedResult, adminKey, vaultId, now);
            if (record.ConflictingSignedResult is not null)
            {
                var conflict = AdminCommandResultVerifier.Verify(issued, record.ConflictingSignedResult, adminKey, vaultId, now);
                if (conflict.Result == first.Result) throw new InvalidDataException("Invalid conflict evidence.");
            }
            return new(first, false, record.ConflictingSignedResult is not null || first.Result == "OutcomeUnknown");
        }
        finally { gate.Release(); }
    }

    public async Task<AdminResultAcceptance> AcceptAsync(IssuedDeviceCommand issued, string signedResult,
        string adminKey, string vaultId, CancellationToken ct = default)
    {
        await gate.WaitAsync(ct);
        try
        {
            long now = time.GetUtcNow().ToUnixTimeMilliseconds();
            var incoming = AdminCommandResultVerifier.Verify(issued, signedResult, adminKey, vaultId, now);
            string path = PathFor(vaultId, incoming.CommandId);
            if (File.Exists(path))
            {
                var previous = Read(path);
                var first = AdminCommandResultVerifier.Verify(issued, previous.FirstSignedResult, adminKey, vaultId, now);
                bool conflict = previous.ConflictingSignedResult is not null;
                if (conflict)
                {
                    var other = AdminCommandResultVerifier.Verify(issued, previous.ConflictingSignedResult!, adminKey, vaultId, now);
                    if (other.Result == first.Result) throw new InvalidDataException("Invalid conflict evidence.");
                }
                if (incoming.Result == first.Result)
                    return new(first, true, conflict || first.Result == "OutcomeUnknown");
                if (!conflict)
                    Write(path, previous with { ConflictingSignedResult = signedResult });
                // Never overwrite a terminal status, including OutcomeUnknown, with a later relay response.
                return new(first, false, true);
            }
            Write(path, new(signedResult));
            return new(incoming, false, incoming.Result == "OutcomeUnknown");
        }
        finally { gate.Release(); }
    }

    private static AdminResultRecord Read(string path)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("DPAPI required.");
        if (new FileInfo(path).Length > 2_000_000) throw new InvalidDataException("Oversized result inbox entry.");
        byte[] plain = ProtectedData.Unprotect(File.ReadAllBytes(path), Entropy, DataProtectionScope.CurrentUser);
        try { return JsonSerializer.Deserialize<AdminResultRecord>(plain) ?? throw new InvalidDataException("Empty result inbox entry."); }
        finally { CryptographicOperations.ZeroMemory(plain); }
    }

    private void Write(string path, AdminResultRecord record)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("DPAPI required.");
        Directory.CreateDirectory(directory);
        string temporary = path + ".tmp-" + Guid.NewGuid().ToString("N");
        byte[] plain = JsonSerializer.SerializeToUtf8Bytes(record);
        try
        {
            byte[] encrypted = ProtectedData.Protect(plain, Entropy, DataProtectionScope.CurrentUser);
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            { stream.Write(encrypted); stream.Flush(true); }
            if (File.Exists(path)) File.Replace(temporary, path, null);
            else File.Move(temporary, path);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plain);
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    private string PathFor(string vaultId, string commandId) => Path.Combine(directory,
        Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new[] { vaultId, commandId }))) + ".result");
}
