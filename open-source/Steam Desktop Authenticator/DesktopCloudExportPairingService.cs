using Newtonsoft.Json;
using System;
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
    internal sealed class DesktopCloudExportPairingService : IDisposable
    {
        private const int MaxFrameSize = 64 * 1024;
        private const string RelayEndpoint = "https://sdaplusplus-pairing-relay.eeswrtdtg4t5.workers.dev";
        private static readonly HttpClient RelayClient = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
        private readonly CloudPairingResult settings;
        private readonly TcpListener listener;
        private readonly ECDiffieHellman keyAgreement;
        private readonly byte[] requestSessionId = RandomNumberGenerator.GetBytes(16);
        private readonly byte[] requestToken = RandomNumberGenerator.GetBytes(32);
        private readonly byte[] responseSessionId = RandomNumberGenerator.GetBytes(16);
        private readonly byte[] responseToken = RandomNumberGenerator.GetBytes(32);
        private readonly CancellationTokenSource lifetime = new CancellationTokenSource(TimeSpan.FromMinutes(2));

        public DesktopCloudExportPairingService(CloudPairingResult settings)
        {
            this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
            keyAgreement = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
            listener = new TcpListener(IPAddress.Any, 0);
            listener.Start();
            VerificationCode = GenerateVerificationCode();
            ExpiresUtc = DateTime.UtcNow.AddMinutes(2);
            PairingUri = BuildPairingUri();
        }

        public string PairingUri { get; }
        public string VerificationCode { get; }
        public DateTime ExpiresUtc { get; }

        public async Task WaitForDeliveryAsync(CancellationToken cancellationToken)
        {
            using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token, cancellationToken);
            Task local = ServeLocalAsync(linked.Token);
            Task relay = ServeRelayAsync(linked.Token);
            Task completed = await Task.WhenAny(local, relay);
            try { await completed; }
            finally { linked.Cancel(); }
        }

        private async Task ServeLocalAsync(CancellationToken token)
        {
            while (true)
            {
                using TcpClient client = await listener.AcceptTcpClientAsync(token);
                try
                {
                    using NetworkStream stream = client.GetStream();
                    byte[] request = await ReadFrameAsync(stream, token);
                    byte[] response = BuildEncryptedResponse(request);
                    await WriteFrameAsync(stream, response, token);
                    CryptographicOperations.ZeroMemory(response);
                    return;
                }
                catch (Exception ex) when (ex is InvalidDataException || ex is JsonException || ex is CryptographicException || ex is FormatException)
                {
                    // Invalid probes do not consume the pairing session.
                }
            }
        }

        private async Task ServeRelayAsync(CancellationToken token)
        {
            byte[] request = await PollRelayAsync(requestSessionId, requestToken, token);
            byte[] response = BuildEncryptedResponse(request);
            try
            {
                string url = RelayUrl(responseSessionId);
                using HttpRequestMessage message = new HttpRequestMessage(HttpMethod.Put, url);
                message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Base64UrlEncode(responseToken));
                message.Content = new ByteArrayContent(response);
                using HttpResponseMessage result = await RelayClient.SendAsync(message, token);
                result.EnsureSuccessStatusCode();
            }
            finally
            {
                CryptographicOperations.ZeroMemory(response);
            }
        }

        private static async Task<byte[]> PollRelayAsync(byte[] sid, byte[] tokenBytes, CancellationToken token)
        {
            while (true)
            {
                try
                {
                    using HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, RelayUrl(sid));
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Base64UrlEncode(tokenBytes));
                    using HttpResponseMessage response = await RelayClient.SendAsync(request, token);
                    if (response.StatusCode == HttpStatusCode.NoContent)
                    {
                        await Task.Delay(1000, token);
                        continue;
                    }
                    if (response.StatusCode == HttpStatusCode.Gone)
                        throw new OperationCanceledException(token);
                    response.EnsureSuccessStatusCode();
                    byte[] result = await response.Content.ReadAsByteArrayAsync(token);
                    if (result.Length == 0 || result.Length > MaxFrameSize) throw new InvalidDataException("Invalid pairing request.");
                    return result;
                }
                catch (HttpRequestException) when (!token.IsCancellationRequested)
                {
                    await Task.Delay(1500, token);
                }
            }
        }

        private byte[] BuildEncryptedResponse(byte[] requestBytes)
        {
            PairingRequest request = JsonConvert.DeserializeObject<PairingRequest>(Encoding.UTF8.GetString(requestBytes));
            if (request == null || request.Version != 2 || request.SessionId != Base64UrlEncode(requestSessionId))
                throw new InvalidDataException("The phone request does not match this pairing session.");

            byte[] publicKey = Convert.FromBase64String(request.PublicKey);
            byte[] shared = null;
            byte[] key = null;
            byte[] plaintext = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(new
            {
                v = 2,
                provider = "webdav",
                url = settings.Url,
                login = settings.Login,
                password = settings.Password,
                remote_path = settings.RemotePath,
                issued_at = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            }));
            try
            {
                using ECDiffieHellman mobileKey = ECDiffieHellman.Create();
                mobileKey.ImportSubjectPublicKeyInfo(publicKey, out _);
                shared = keyAgreement.DeriveRawSecretAgreement(mobileKey.PublicKey);
                key = HkdfSha256(shared, requestSessionId, Encoding.UTF8.GetBytes("SDA++ local pairing v2|" + VerificationCode), 32);
                byte[] nonce = RandomNumberGenerator.GetBytes(12);
                byte[] ciphertext = new byte[plaintext.Length];
                byte[] tag = new byte[16];
                using AesGcm aes = new AesGcm(key, 16);
                aes.Encrypt(nonce, plaintext, ciphertext, tag, requestSessionId);
                return Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(new
                {
                    v = 2,
                    sid = Base64UrlEncode(requestSessionId),
                    nonce = Convert.ToBase64String(nonce),
                    ciphertext = Convert.ToBase64String(ciphertext),
                    tag = Convert.ToBase64String(tag)
                }));
            }
            finally
            {
                CryptographicOperations.ZeroMemory(plaintext);
                if (shared != null) CryptographicOperations.ZeroMemory(shared);
                if (key != null) CryptographicOperations.ZeroMemory(key);
            }
        }

        private string BuildPairingUri()
        {
            var descriptor = new
            {
                v = 2,
                direction = "to-mobile",
                sid = Base64UrlEncode(requestSessionId),
                port = ((IPEndPoint)listener.LocalEndpoint).Port,
                hosts = GetLanAddresses(),
                relay = RelayEndpoint,
                relay_token = Base64UrlEncode(requestToken),
                response_sid = Base64UrlEncode(responseSessionId),
                response_token = Base64UrlEncode(responseToken),
                pk = Convert.ToBase64String(keyAgreement.ExportSubjectPublicKeyInfo()),
                exp = new DateTimeOffset(ExpiresUtc).ToUnixTimeSeconds()
            };
            return "sdapp-pair://v2?p=" + Base64UrlEncode(Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(descriptor)));
        }

        public void Dispose()
        {
            lifetime.Cancel();
            listener.Stop();
            keyAgreement.Dispose();
            lifetime.Dispose();
            settings.Password = string.Empty;
            CryptographicOperations.ZeroMemory(requestSessionId);
            CryptographicOperations.ZeroMemory(requestToken);
            CryptographicOperations.ZeroMemory(responseSessionId);
            CryptographicOperations.ZeroMemory(responseToken);
        }

        private static string[] GetLanAddresses() => NetworkInterface.GetAllNetworkInterfaces()
            .Where(adapter => adapter.OperationalStatus == OperationalStatus.Up &&
                (adapter.NetworkInterfaceType == NetworkInterfaceType.Ethernet || adapter.NetworkInterfaceType == NetworkInterfaceType.Wireless80211))
            .SelectMany(adapter => adapter.GetIPProperties().UnicastAddresses)
            .Where(address => address.Address.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(address.Address))
            .Select(address => address.Address.ToString()).Distinct().ToArray();

        private static string GenerateVerificationCode()
        {
            const string alphabet = "23456789ABCDEFGHJKLMNPQRSTUVWXYZ";
            byte[] random = RandomNumberGenerator.GetBytes(8);
            try { return new string(random.Select(value => alphabet[value % alphabet.Length]).ToArray()); }
            finally { CryptographicOperations.ZeroMemory(random); }
        }

        private static string RelayUrl(byte[] sid) => RelayEndpoint + "/v1/pair/" + Base64UrlEncode(sid);
        private static string Base64UrlEncode(byte[] value) => Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');

        private static async Task<byte[]> ReadFrameAsync(Stream stream, CancellationToken token)
        {
            byte[] length = new byte[4];
            await ReadExactlyAsync(stream, length, token);
            int size = IPAddress.NetworkToHostOrder(BitConverter.ToInt32(length, 0));
            if (size <= 0 || size > MaxFrameSize) throw new InvalidDataException("Invalid frame size.");
            byte[] payload = new byte[size];
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
            await stream.WriteAsync(BitConverter.GetBytes(IPAddress.HostToNetworkOrder(payload.Length)), token);
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
            while (offset < length)
            {
                using HMACSHA256 expand = new HMACSHA256(prk);
                previous = expand.ComputeHash(previous.Concat(info).Append(counter++).ToArray());
                int count = Math.Min(previous.Length, length - offset);
                Buffer.BlockCopy(previous, 0, output, offset, count);
                offset += count;
            }
            CryptographicOperations.ZeroMemory(prk);
            return output;
        }

        private sealed class PairingRequest
        {
            [JsonProperty("v")] public int Version { get; set; }
            [JsonProperty("sid")] public string SessionId { get; set; }
            [JsonProperty("public_key")] public string PublicKey { get; set; }
        }
    }
}
