using System.Security.Cryptography;

namespace SDAPlusPlus.Core.Devices;

public sealed record IssuedDeviceCommand(string Envelope, string RecipientPublicKey);

public static class AdminCommandResultVerifier
{
    // Both inputs describing the original command must come from durable local state, not Worker metadata.
    public static DeviceResultV2 Verify(IssuedDeviceCommand issued, string signedResult, string adminPublicKey, string vaultId, long now)
    {
        var command = EndToEndTrust.Verify<DeviceCommandV2>(issued.Envelope, adminPublicKey);
        if (command.Version != 2 || command.VaultId != vaultId || command.IssuerDeviceId != EndToEndTrust.DeviceId(adminPublicKey) ||
            command.TargetDeviceId != EndToEndTrust.DeviceId(issued.RecipientPublicKey))
            throw new CryptographicException("Invalid locally issued command context.");
        return EndToEndTrust.ValidateResult(signedResult, issued.RecipientPublicKey, command, issued.Envelope, now);
    }
}
