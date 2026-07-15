using System;
using System.Buffers.Binary;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace Steam_Desktop_Authenticator
{
    internal sealed class RemoteActionEnvelope
    {
        public byte[] WrappedKey { get; init; }
        public byte[] Nonce { get; init; }
        public byte[] Ciphertext { get; init; }
        public byte[] Tag { get; init; }
    }

    internal static class RemoteActionEnvelopeCodec
    {
        private static readonly byte[] Magic = { (byte)'S', (byte)'D', (byte)'A', (byte)'R' };
        private const byte Version = 1;
        private const int NonceSize = 12;
        private const int TagSize = 16;
        private const int MaximumCiphertextSize = 96 * 1024;

        public static string Encode(RemoteActionEnvelope envelope)
        {
            if (envelope?.WrappedKey == null || envelope.WrappedKey.Length < 128 || envelope.WrappedKey.Length > 512)
                throw new InvalidDataException("Invalid wrapped action key.");
            if (envelope.Nonce?.Length != NonceSize || envelope.Tag?.Length != TagSize)
                throw new InvalidDataException("Invalid remote action envelope.");
            if (envelope.Ciphertext == null || envelope.Ciphertext.Length > MaximumCiphertextSize)
                throw new InvalidDataException("Remote action payload is too large.");
            byte[] output = new byte[7 + NonceSize + envelope.WrappedKey.Length + envelope.Ciphertext.Length + TagSize];
            Magic.CopyTo(output, 0);
            output[4] = Version;
            BinaryPrimitives.WriteUInt16BigEndian(output.AsSpan(5, 2), (ushort)envelope.WrappedKey.Length);
            int offset = 7;
            envelope.Nonce.CopyTo(output, offset); offset += NonceSize;
            envelope.WrappedKey.CopyTo(output, offset); offset += envelope.WrappedKey.Length;
            envelope.Ciphertext.CopyTo(output, offset); offset += envelope.Ciphertext.Length;
            envelope.Tag.CopyTo(output, offset);
            return Base64Url(output);
        }

        public static RemoteActionEnvelope Decode(string value)
        {
            if (string.IsNullOrEmpty(value) || value.Length < 32 || value.Length > 128 * 1024 ||
                !Regex.IsMatch(value, "^[A-Za-z0-9_-]+$")) throw new InvalidDataException("Invalid remote action envelope.");
            byte[] input = Base64UrlDecode(value);
            if (input.Length < 7 + NonceSize + 128 + TagSize || !input.AsSpan(0, 4).SequenceEqual(Magic) || input[4] != Version)
                throw new InvalidDataException("Unsupported remote action envelope.");
            int wrappedKeySize = BinaryPrimitives.ReadUInt16BigEndian(input.AsSpan(5, 2));
            if (wrappedKeySize < 128 || wrappedKeySize > 512 || input.Length < 7 + NonceSize + wrappedKeySize + TagSize)
                throw new InvalidDataException("Invalid wrapped action key.");
            int ciphertextSize = input.Length - 7 - NonceSize - wrappedKeySize - TagSize;
            if (ciphertextSize < 0 || ciphertextSize > MaximumCiphertextSize)
                throw new InvalidDataException("Remote action payload is too large.");
            int offset = 7;
            byte[] nonce = input.AsSpan(offset, NonceSize).ToArray(); offset += NonceSize;
            byte[] wrappedKey = input.AsSpan(offset, wrappedKeySize).ToArray(); offset += wrappedKeySize;
            byte[] ciphertext = input.AsSpan(offset, ciphertextSize).ToArray(); offset += ciphertextSize;
            byte[] tag = input.AsSpan(offset, TagSize).ToArray();
            return new RemoteActionEnvelope { WrappedKey = wrappedKey, Nonce = nonce, Ciphertext = ciphertext, Tag = tag };
        }

        public static byte[] AssociatedData(string vaultId, string actionId, string direction)
        {
            if (!Regex.IsMatch(vaultId ?? string.Empty, "^[A-Za-z0-9_-]{22}$")) throw new ArgumentException("Invalid device registry ID.");
            if (!Guid.TryParseExact(actionId, "D", out _)) throw new ArgumentException("Invalid remote action ID.");
            if (direction != "request" && direction != "response") throw new ArgumentException("Invalid remote action direction.");
            StringBuilder value = new StringBuilder("sdapp-remote-action/v1\n");
            AddField(value, "vaultId", vaultId);
            AddField(value, "actionId", actionId);
            AddField(value, "direction", direction);
            return Encoding.UTF8.GetBytes(value.ToString());
        }

        private static void AddField(StringBuilder builder, string name, string value) =>
            builder.Append(name).Append('=').Append(Encoding.UTF8.GetByteCount(value)).Append(':').Append(value).Append('\n');

        private static string Base64Url(byte[] value) => Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        private static byte[] Base64UrlDecode(string value)
        {
            string padded = value.Replace('-', '+').Replace('_', '/');
            padded += new string('=', (4 - padded.Length % 4) % 4);
            return Convert.FromBase64String(padded);
        }
    }
}
