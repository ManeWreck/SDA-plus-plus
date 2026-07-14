using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Steam_Desktop_Authenticator
{
    internal sealed class CloudPairingResult
    {
        public string Url { get; set; }
        public string Login { get; set; }
        public string Password { get; set; }
        public string RemotePath { get; set; }
    }

    internal sealed class LocalCloudPairingService : IDisposable
    {
        private const int MaxFrameSize = 64 * 1024;
        private const string RelayEndpoint = "https://sdaplusplus-pairing-relay.eeswrtdtg4t5.workers.dev";
        private static readonly byte[] HkdfInfo = Encoding.UTF8.GetBytes("SDA++ local pairing v1");
        private static readonly HttpClient RelayClient = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
        private readonly TcpListener listener;
        private readonly ECDiffieHellman keyAgreement;
        private readonly byte[] sessionId;
        private readonly byte[] relayToken;
        private readonly CancellationTokenSource lifetime = new CancellationTokenSource(TimeSpan.FromMinutes(2));

        public LocalCloudPairingService()
        {
            sessionId = RandomNumberGenerator.GetBytes(16);
            relayToken = RandomNumberGenerator.GetBytes(32);
            keyAgreement = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
            listener = new TcpListener(IPAddress.Any, 0);
            listener.Start();
            ExpiresUtc = DateTime.UtcNow.AddMinutes(2);
            PairingUri = BuildPairingUri();
        }

        public string PairingUri { get; }
        public DateTime ExpiresUtc { get; }

        public async Task<CloudPairingResult> WaitForResultAsync(CancellationToken cancellationToken)
        {
            using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token, cancellationToken);
            Task<CloudPairingResult> localTask = WaitForLocalResultAsync(linked.Token);
            Task<CloudPairingResult> relayTask = WaitForRelayResultAsync(linked.Token);
            Task<CloudPairingResult> completed = await Task.WhenAny(localTask, relayTask);
            try
            {
                return await completed;
            }
            finally
            {
                linked.Cancel();
            }
        }

        private async Task<CloudPairingResult> WaitForLocalResultAsync(CancellationToken cancellationToken)
        {
            while (true)
            {
                using TcpClient client = await listener.AcceptTcpClientAsync(cancellationToken);
                using CancellationTokenSource clientTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                clientTimeout.CancelAfter(TimeSpan.FromSeconds(15));
                try
                {
                    return await ReceiveResultAsync(client, clientTimeout.Token);
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    // Ignore a stalled LAN probe and keep the one-time listener available.
                }
                catch (Exception ex) when (ex is EndOfStreamException || ex is InvalidDataException ||
                                           ex is JsonException || ex is CryptographicException ||
                                           ex is InvalidOperationException || ex is FormatException)
                {
                    // An invalid connection must not consume the visible pairing QR.
                }
            }
        }

        private async Task<CloudPairingResult> WaitForRelayResultAsync(CancellationToken cancellationToken)
        {
            string relayUrl = RelayEndpoint.TrimEnd('/') + "/v1/pair/" + Base64UrlEncode(sessionId);
            string token = Base64UrlEncode(relayToken);
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    using HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, relayUrl);
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                    using HttpResponseMessage response = await RelayClient.SendAsync(request, HttpCompletionOption.ResponseContentRead, cancellationToken);
                    if (response.StatusCode == HttpStatusCode.NoContent)
                    {
                        await Task.Delay(1000, cancellationToken);
                        continue;
                    }
                    if (response.StatusCode == HttpStatusCode.Gone)
                        throw new OperationCanceledException("The relay pairing session expired.", cancellationToken);
                    if (response.IsSuccessStatusCode)
                    {
                        byte[] frame = await response.Content.ReadAsByteArrayAsync(cancellationToken);
                        if (frame.Length <= 0 || frame.Length > MaxFrameSize)
                            throw new InvalidDataException("Invalid relay pairing frame size.");
                        return DecryptEnvelope(frame);
                    }
                }
                catch (HttpRequestException) when (!cancellationToken.IsCancellationRequested)
                {
                    // Internet relay is optional; keep polling while LAN pairing remains active.
                }
                await Task.Delay(1500, cancellationToken);
            }
        }

        private async Task<CloudPairingResult> ReceiveResultAsync(TcpClient client, CancellationToken cancellationToken)
        {
            using NetworkStream stream = client.GetStream();
            byte[] frame = await ReadFrameAsync(stream, cancellationToken);
            try
            {
                CloudPairingResult result = DecryptEnvelope(frame);
                await WriteFrameAsync(stream, Encoding.UTF8.GetBytes("{\"ok\":true}"), cancellationToken);
                return result;
            }
            catch
            {
                try { await WriteFrameAsync(stream, Encoding.UTF8.GetBytes("{\"ok\":false}"), cancellationToken); } catch { }
                throw;
            }
        }

        private CloudPairingResult DecryptEnvelope(byte[] frame)
        {
            PairingEnvelope envelope = JsonConvert.DeserializeObject<PairingEnvelope>(Encoding.UTF8.GetString(frame));
            if (envelope == null || envelope.Version != 1 || !FixedTimeEquals(envelope.SessionId, Base64UrlEncode(sessionId)))
            {
                throw new InvalidOperationException("The pairing response does not match this one-time session.");
            }

            byte[] mobilePublicKey = Convert.FromBase64String(envelope.PublicKey);
            byte[] nonce = Convert.FromBase64String(envelope.Nonce);
            byte[] ciphertext = Convert.FromBase64String(envelope.Ciphertext);
            byte[] tag = Convert.FromBase64String(envelope.Tag);
            byte[] sharedSecret = null;
            byte[] aesKey = null;
            byte[] plaintext = new byte[ciphertext.Length];
            try
            {
                using ECDiffieHellman mobileKey = ECDiffieHellman.Create();
                mobileKey.ImportSubjectPublicKeyInfo(mobilePublicKey, out _);
                sharedSecret = keyAgreement.DeriveRawSecretAgreement(mobileKey.PublicKey);
                aesKey = HkdfSha256(sharedSecret, sessionId, HkdfInfo, 32);
                using AesGcm aes = new AesGcm(aesKey, 16);
                aes.Decrypt(nonce, ciphertext, tag, plaintext, sessionId);
                PairingPayload payload = JsonConvert.DeserializeObject<PairingPayload>(Encoding.UTF8.GetString(plaintext));
                return ValidatePayload(payload);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(plaintext);
                if (sharedSecret != null) CryptographicOperations.ZeroMemory(sharedSecret);
                if (aesKey != null) CryptographicOperations.ZeroMemory(aesKey);
            }
        }

        public void Dispose()
        {
            lifetime.Cancel();
            listener.Stop();
            keyAgreement.Dispose();
            lifetime.Dispose();
            CryptographicOperations.ZeroMemory(sessionId);
            CryptographicOperations.ZeroMemory(relayToken);
        }

        private string BuildPairingUri()
        {
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            var descriptor = new
            {
                v = 1,
                sid = Base64UrlEncode(sessionId),
                port,
                hosts = GetLanAddresses(),
                relay = RelayEndpoint,
                relay_token = Base64UrlEncode(relayToken),
                pk = Convert.ToBase64String(keyAgreement.ExportSubjectPublicKeyInfo()),
                exp = new DateTimeOffset(ExpiresUtc).ToUnixTimeSeconds()
            };
            string encoded = Base64UrlEncode(Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(descriptor)));
            return "sdapp-pair://v1?p=" + encoded;
        }

        private static string[] GetLanAddresses()
        {
            return NetworkInterface.GetAllNetworkInterfaces()
                .Where(IsUsableLanAdapter)
                .SelectMany(adapter => adapter.GetIPProperties().UnicastAddresses)
                .Where(address => address.Address.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(address.Address))
                .Select(address => address.Address.ToString())
                .Distinct()
                .OrderByDescending(IsPrivateAddress)
                .ToArray();
        }

        private static bool IsUsableLanAdapter(NetworkInterface adapter)
        {
            if (adapter.OperationalStatus != OperationalStatus.Up ||
                adapter.NetworkInterfaceType != NetworkInterfaceType.Ethernet &&
                adapter.NetworkInterfaceType != NetworkInterfaceType.Wireless80211)
            {
                return false;
            }

            IPInterfaceProperties properties = adapter.GetIPProperties();
            if (!properties.GatewayAddresses.Any(gateway =>
                    gateway.Address.AddressFamily == AddressFamily.InterNetwork &&
                    !gateway.Address.Equals(IPAddress.Any)))
            {
                return false;
            }

            string identity = (adapter.Name + " " + adapter.Description).ToLowerInvariant();
            string[] virtualMarkers = { "radmin", "vpn", "zerotier", "tap", "tunnel", "virtual", "hyper-v", "vmware", "vbox" };
            return !virtualMarkers.Any(identity.Contains);
        }

        private static bool IsPrivateAddress(string value)
        {
            byte[] bytes = IPAddress.Parse(value).GetAddressBytes();
            return bytes[0] == 10 || bytes[0] == 192 && bytes[1] == 168 || bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31;
        }

        private static CloudPairingResult ValidatePayload(PairingPayload payload)
        {
            if (payload == null || payload.Version != 1 || !string.Equals(payload.Provider, "webdav", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Only WebDAV pairing is supported in this version.");
            if (!Uri.TryCreate(payload.Url, UriKind.Absolute, out Uri uri) || uri.Scheme != Uri.UriSchemeHttps)
                throw new InvalidOperationException("The phone supplied an invalid WebDAV HTTPS URL.");
            if (string.IsNullOrWhiteSpace(payload.Login) || string.IsNullOrEmpty(payload.Password))
                throw new InvalidOperationException("The phone has incomplete WebDAV credentials.");
            long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            if (Math.Abs(now - payload.IssuedAt) > 180)
                throw new InvalidOperationException("The pairing response has expired.");
            return new CloudPairingResult
            {
                Url = payload.Url.Trim(), Login = payload.Login.Trim(), Password = payload.Password,
                RemotePath = string.IsNullOrWhiteSpace(payload.RemotePath) ? "SDAppVault" : payload.RemotePath.Trim()
            };
        }

        private static async Task<byte[]> ReadFrameAsync(Stream stream, CancellationToken token)
        {
            byte[] lengthBytes = new byte[4];
            await ReadExactlyAsync(stream, lengthBytes, token);
            int length = IPAddress.NetworkToHostOrder(BitConverter.ToInt32(lengthBytes, 0));
            if (length <= 0 || length > MaxFrameSize) throw new InvalidDataException("Invalid pairing frame size.");
            byte[] payload = new byte[length];
            await ReadExactlyAsync(stream, payload, token);
            return payload;
        }

        private static async Task ReadExactlyAsync(Stream stream, byte[] buffer, CancellationToken token)
        {
            int offset = 0;
            while (offset < buffer.Length)
            {
                int read = await stream.ReadAsync(buffer.AsMemory(offset), token);
                if (read == 0) throw new EndOfStreamException();
                offset += read;
            }
        }

        private static async Task WriteFrameAsync(Stream stream, byte[] payload, CancellationToken token)
        {
            byte[] length = BitConverter.GetBytes(IPAddress.HostToNetworkOrder(payload.Length));
            await stream.WriteAsync(length, token);
            await stream.WriteAsync(payload, token);
            await stream.FlushAsync(token);
        }

        private static byte[] HkdfSha256(byte[] input, byte[] salt, byte[] info, int length)
        {
            byte[] prk;
            using (HMACSHA256 extract = new HMACSHA256(salt)) prk = extract.ComputeHash(input);
            byte[] output = new byte[length];
            byte[] previous = Array.Empty<byte>();
            int offset = 0;
            byte counter = 1;
            using HMACSHA256 expand = new HMACSHA256(prk);
            while (offset < length)
            {
                byte[] blockInput = previous.Concat(info).Append(counter++).ToArray();
                previous = expand.ComputeHash(blockInput);
                int count = Math.Min(previous.Length, length - offset);
                Buffer.BlockCopy(previous, 0, output, offset, count);
                offset += count;
            }
            CryptographicOperations.ZeroMemory(prk);
            return output;
        }

        private static string Base64UrlEncode(byte[] value) => Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        private static bool FixedTimeEquals(string left, string right) => CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(left ?? ""), Encoding.UTF8.GetBytes(right ?? ""));

        private sealed class PairingEnvelope
        {
            [JsonProperty("v")] public int Version { get; set; }
            [JsonProperty("sid")] public string SessionId { get; set; }
            [JsonProperty("public_key")] public string PublicKey { get; set; }
            [JsonProperty("nonce")] public string Nonce { get; set; }
            [JsonProperty("ciphertext")] public string Ciphertext { get; set; }
            [JsonProperty("tag")] public string Tag { get; set; }
        }

        private sealed class PairingPayload
        {
            [JsonProperty("v")] public int Version { get; set; }
            [JsonProperty("provider")] public string Provider { get; set; }
            [JsonProperty("url")] public string Url { get; set; }
            [JsonProperty("login")] public string Login { get; set; }
            [JsonProperty("password")] public string Password { get; set; }
            [JsonProperty("remote_path")] public string RemotePath { get; set; }
            [JsonProperty("issued_at")] public long IssuedAt { get; set; }
        }
    }
}
