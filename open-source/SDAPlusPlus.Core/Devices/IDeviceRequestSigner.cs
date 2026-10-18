namespace SDAPlusPlus.Core.Devices;

public interface IDeviceRequestSigner
{
    string DeviceId { get; }
    string PublicKey { get; }
    string EncryptionPublicKey { get; }
    string EncryptionKeySignature { get; }
    string Protection { get; }
    string Sign(byte[] payload);
    byte[] UnwrapTrustKey(byte[] wrapped) => throw new NotSupportedException("E2E decryption is unavailable.");
}
