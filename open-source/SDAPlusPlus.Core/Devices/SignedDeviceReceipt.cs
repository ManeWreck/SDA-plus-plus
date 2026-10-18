using System.Security.Cryptography;

namespace SDAPlusPlus.Core.Devices;

public static class SignedDeviceReceipt
{
    public static string Create(DeviceExecutionReceipt receipt, string vaultId, string adminPublicKey,
        IDeviceRequestSigner recipient, long now)
    {
        var entry = receipt.Command;
        if (entry.SignedEnvelope is null ||
            receipt.State is not (DeviceExecutionState.Completed or DeviceExecutionState.Failed or DeviceExecutionState.OutcomeUnknown))
            throw new CryptographicException("A signed command and final journal receipt are required.");
        var command = EndToEndTrust.Verify<DeviceCommandV2>(entry.SignedEnvelope, adminPublicKey);
        // Expiry is deliberately not compared with now: a final result remains reportable after expiry.
        if (command.Version != 2 || command.IssuerDeviceId != EndToEndTrust.DeviceId(adminPublicKey) ||
            command.TargetDeviceId != recipient.DeviceId || entry.DeviceId != recipient.DeviceId ||
            command.VaultId != vaultId || entry.VaultId != vaultId || command.CommandId != entry.CommandId ||
            !Guid.TryParseExact(command.CommandId, "D", out _) || command.CommandId != command.CommandId.ToLowerInvariant() ||
            command.CommandType != entry.Type || command.ExpiresAt != entry.ExpiresAt ||
            command.CommandType is not ("Lock" or "WipeLocalData" or "TerminateSteamSessions") ||
            command.RequiredCapability != "ManageDevices" || command.TrustRevision <= 0 ||
            !Guid.TryParseExact(command.GrantId, "D", out _) || command.IssuedAt <= 0 || command.IssuedAt > now ||
            command.ExpiresAt <= command.IssuedAt || command.ExpiresAt - command.IssuedAt > 86_400_000)
            throw new CryptographicException("Journal receipt does not match the signed command context.");
        return EndToEndTrust.Sign(new DeviceResultV2(2, vaultId, command.CommandId,
            EndToEndTrust.Fingerprint(entry.SignedEnvelope), recipient.DeviceId, command.IssuerDeviceId,
            receipt.State.ToString(), now, command.GrantId, command.TrustRevision), recipient);
    }
}
