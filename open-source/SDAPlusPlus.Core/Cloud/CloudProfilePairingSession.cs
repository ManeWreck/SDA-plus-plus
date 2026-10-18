using System.Buffers.Binary;
using System.Net;
using System.Net.Http.Headers;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SDAPlusPlus.Core.Devices;

namespace SDAPlusPlus.Core.Cloud;

// Wire-compatible with Mobile DesktopPairingClient (sdapp-pair v2).
public sealed class CloudProfilePairingSession : IDisposable
{
    private const int Limit = 65536;
    private readonly HttpClient _http;
    private readonly string _relay;
    private readonly CloudPairingProfile? _outgoing;
    private readonly TcpListener _listener;
    private readonly ECDiffieHellman _agreement = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
    private readonly byte[] _sid = RandomNumberGenerator.GetBytes(16);
    private readonly byte[] _token = RandomNumberGenerator.GetBytes(32);
    private readonly byte[] _responseSid = RandomNumberGenerator.GetBytes(16);
    private readonly byte[] _responseToken = RandomNumberGenerator.GetBytes(32);
    private readonly CancellationTokenSource _lifetime = new(TimeSpan.FromMinutes(2));
    private readonly object _cryptoLock = new();
    private int _started;

    public CloudProfilePairingSession(HttpClient http, string relay, IDeviceRequestSigner signer, CloudPairingProfile? outgoing = null)
    {
        if (!Uri.TryCreate(relay, UriKind.Absolute, out var uri) || uri.Scheme != "https" ||
            uri.AbsolutePath != "/" || uri.Query.Length != 0 || uri.Fragment.Length != 0 || uri.UserInfo.Length != 0)
            throw new ArgumentException("Pairing relay must be an HTTPS root URL.");
        outgoing?.Validate();
        _http = http;
        _relay = uri.GetLeftPart(UriPartial.Authority);
        _outgoing = outgoing;
        _listener = new TcpListener(IPAddress.Any, 0);
        _listener.Start();
        const string alphabet = "23456789ABCDEFGHJKLMNPQRSTUVWXYZ";
        VerificationCode = string.Concat(Enumerable.Range(0, 8).Select(_ => alphabet[RandomNumberGenerator.GetInt32(alphabet.Length)]));
        ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(2);
        string sid = Encode(_sid), pk = Convert.ToBase64String(_agreement.ExportSubjectPublicKeyInfo());
        long expiry = ExpiresAt.ToUnixTimeSeconds();
        var descriptor = new JObject
        {
            ["v"] = 2, ["direction"] = outgoing is null ? "to-desktop" : "to-mobile",
            ["sid"] = sid, ["pk"] = pk, ["exp"] = expiry,
            ["hosts"] = new JArray(LanAddresses()), ["port"] = ((IPEndPoint)_listener.LocalEndpoint).Port,
            ["relay"] = _relay, ["relay_token"] = Encode(_token),
            ["device_id"] = signer.DeviceId, ["device_pk"] = signer.PublicKey,
            ["device_key_protection"] = signer.Protection,
            ["device_signature"] = signer.Sign(Encoding.UTF8.GetBytes($"sdapp-pairing-key/v1\n{sid}\n{pk}\n{expiry}\n"))
        };
        if (outgoing is not null)
        {
            descriptor["response_sid"] = Encode(_responseSid);
            descriptor["response_token"] = Encode(_responseToken);
        }
        PairingUri = "sdapp-pair://v2?p=" + Encode(Encoding.UTF8.GetBytes(descriptor.ToString(Formatting.None)));
    }

    public string PairingUri { get; }
    public string VerificationCode { get; }
    public DateTimeOffset ExpiresAt { get; }

    public async Task<CloudPairingProfile?> RunAsync(CancellationToken cancellationToken = default)
    {
        if (Interlocked.Exchange(ref _started, 1) != 0) throw new InvalidOperationException("Pairing is one-time.");
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
        Task<CloudPairingProfile?> local = ServeLocalAsync(linked.Token), relay = ServeRelayAsync(linked.Token);
        var pending = new List<Task<CloudPairingProfile?>> { local, relay };
        try
        {
            while (pending.Count > 0)
            {
                var completed = await Task.WhenAny(pending);
                pending.Remove(completed);
                try { return await completed; }
                catch (Exception) when (pending.Count > 0 && !linked.IsCancellationRequested)
                { /* A failed transport must not interrupt the other one. */ }
            }
            throw new IOException("Pairing transports are unavailable.");
        }
        finally
        {
            linked.Cancel();
            _listener.Stop();
            // Observe both tasks before disposing cryptographic material.
            try { await Task.WhenAll(local, relay); } catch { }
        }
    }

    private async Task<CloudPairingProfile?> ServeLocalAsync(CancellationToken token)
    {
        while (true)
        {
            using var client = await _listener.AcceptTcpClientAsync(token);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeout.CancelAfter(TimeSpan.FromSeconds(12));
            try
            {
                using var stream = client.GetStream();
                byte[] frame = await ReadFrameAsync(stream, timeout.Token);
                var (profile, response) = Process(frame);
                await WriteFrameAsync(stream, response, timeout.Token);
                return profile;
            }
            catch (OperationCanceledException) when (!token.IsCancellationRequested) { }
            catch (Exception ex) when (ex is IOException or JsonException or CryptographicException or FormatException or ArgumentException or InvalidOperationException)
            { /* Invalid or stalled probes do not consume the displayed QR. */ }
        }
    }

    private async Task<CloudPairingProfile?> ServeRelayAsync(CancellationToken token)
    {
        while (true)
        {
            try
            {
                using var request = Request(HttpMethod.Get, _sid, _token);
                using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
                if (response.StatusCode == HttpStatusCode.Gone) throw new IOException("Pairing session expired.");
                if (response.StatusCode == HttpStatusCode.OK)
                {
                    await using var stream = await response.Content.ReadAsStreamAsync(token);
                    using var buffer = new MemoryStream();
                    byte[] chunk = new byte[4096];
                    int read;
                    while ((read = await stream.ReadAsync(chunk, token)) > 0)
                    {
                        if (buffer.Length + read > Limit) throw new InvalidDataException("Pairing frame too large.");
                        buffer.Write(chunk, 0, read);
                    }
                    var (profile, reply) = Process(buffer.ToArray());
                    if (_outgoing is not null)
                    {
                        using var put = Request(HttpMethod.Put, _responseSid, _responseToken);
                        put.Content = new ByteArrayContent(reply);
                        using var sent = await _http.SendAsync(put, token);
                        sent.EnsureSuccessStatusCode();
                    }
                    return profile;
                }
            }
            catch (HttpRequestException) when (!token.IsCancellationRequested) { }
            catch (OperationCanceledException) when (!token.IsCancellationRequested) { }
            await Task.Delay(1000, token);
        }
    }

    private HttpRequestMessage Request(HttpMethod method, byte[] sid, byte[] token)
    {
        var request = new HttpRequestMessage(method, _relay + "/v1/pair/" + Encode(sid));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Encode(token));
        return request;
    }

    private (CloudPairingProfile? Profile, byte[] Response) Process(byte[] frame)
    {
        if (frame.Length is 0 or > Limit) throw new InvalidDataException("Invalid pairing frame.");
        var json = JObject.Parse(Encoding.UTF8.GetString(frame), new JsonLoadSettings { DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error });
        if (json.Value<int>("v") != 2 || json.Value<string>("sid") != Encode(_sid)) throw new InvalidDataException("Wrong pairing session.");
        byte[] publicKey = Convert.FromBase64String(json.Value<string>("public_key") ?? "");
        using var peer = ECDiffieHellman.Create();
        peer.ImportSubjectPublicKeyInfo(publicKey, out int consumed);
        if (consumed != publicKey.Length || peer.ExportParameters(false).Curve.Oid.Value != "1.2.840.10045.3.1.7")
            throw new CryptographicException("Pairing requires P-256.");
        byte[] shared;
        lock (_cryptoLock) shared = _agreement.DeriveRawSecretAgreement(peer.PublicKey);
        byte[] key = HKDF.DeriveKey(HashAlgorithmName.SHA256, shared, 32, _sid, Encoding.UTF8.GetBytes("SDA++ local pairing v2|" + VerificationCode));
        byte[] plaintext = [];
        try
        {
            using var aes = new AesGcm(key, 16);
            if (_outgoing is null)
            {
                byte[] cipher = Convert.FromBase64String(json.Value<string>("ciphertext") ?? "");
                plaintext = new byte[cipher.Length];
                aes.Decrypt(Convert.FromBase64String(json.Value<string>("nonce") ?? ""), cipher,
                    Convert.FromBase64String(json.Value<string>("tag") ?? ""), plaintext, _sid);
                var payload = JObject.Parse(Encoding.UTF8.GetString(plaintext), new JsonLoadSettings { DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error });
                var profile = CloudPairingProfile.FromJson(payload);
                return (profile, Encoding.UTF8.GetBytes("{\"ok\":true}"));
            }
            plaintext = Encoding.UTF8.GetBytes(_outgoing.ToJson().ToString(Formatting.None));
            byte[] nonce = RandomNumberGenerator.GetBytes(12), ciphertext = new byte[plaintext.Length], tag = new byte[16];
            aes.Encrypt(nonce, plaintext, ciphertext, tag, _sid);
            return (null, Encoding.UTF8.GetBytes(new JObject
            {
                ["v"] = 2, ["sid"] = Encode(_sid), ["nonce"] = Convert.ToBase64String(nonce),
                ["ciphertext"] = Convert.ToBase64String(ciphertext), ["tag"] = Convert.ToBase64String(tag)
            }.ToString(Formatting.None)));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(shared);
            CryptographicOperations.ZeroMemory(key);
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    private static async Task<byte[]> ReadFrameAsync(Stream stream, CancellationToken token)
    {
        byte[] header = new byte[4];
        await stream.ReadExactlyAsync(header, token);
        int length = BinaryPrimitives.ReadInt32BigEndian(header);
        if (length is <= 0 or > Limit) throw new InvalidDataException("Invalid pairing frame size.");
        byte[] frame = new byte[length];
        await stream.ReadExactlyAsync(frame, token);
        return frame;
    }
    private static async Task WriteFrameAsync(Stream stream, byte[] frame, CancellationToken token)
    {
        byte[] header = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(header, frame.Length);
        await stream.WriteAsync(header, token);
        await stream.WriteAsync(frame, token);
        await stream.FlushAsync(token);
    }
    private static IEnumerable<string> LanAddresses() => NetworkInterface.GetAllNetworkInterfaces()
        .Where(a => a.OperationalStatus == OperationalStatus.Up && a.NetworkInterfaceType is NetworkInterfaceType.Ethernet or NetworkInterfaceType.Wireless80211)
        .SelectMany(a => a.GetIPProperties().UnicastAddresses)
        .Where(a => a.Address.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(a.Address))
        .Select(a => a.Address.ToString()).Distinct().Take(8);
    private static string Encode(byte[] data) => Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    public void Dispose()
    {
        _lifetime.Cancel();
        _listener.Stop();
        _agreement.Dispose();
        _lifetime.Dispose();
        CryptographicOperations.ZeroMemory(_token);
        CryptographicOperations.ZeroMemory(_responseToken);
    }
}

public sealed record CloudPairingProfile(string Url, string Login, string Password, string RemotePath,
    string CredentialsKey = "", string CredentialsRemotePath = "", string RelayMode = "managed", string RelayUrl = "")
{
    public void Validate()
    {
        if (!Uri.TryCreate(Url, UriKind.Absolute, out var uri) || uri.Scheme != "https" || uri.UserInfo.Length != 0 ||
            string.IsNullOrWhiteSpace(Login) || string.IsNullOrEmpty(Password))
            throw new InvalidDataException("A complete HTTPS WebDAV profile is required.");
    }
    public JObject ToJson() => new()
    {
        ["v"] = 2, ["provider"] = "webdav", ["url"] = Url, ["login"] = Login, ["password"] = Password,
        ["remote_path"] = RemotePath, ["sync_credentials"] = CredentialsKey.Length > 0,
        ["credentials_key"] = CredentialsKey, ["credentials_remote_path"] = CredentialsRemotePath,
        ["pairing_relay_mode"] = RelayMode, ["pairing_relay_url"] = RelayUrl,
        ["issued_at"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
    };
    public static CloudPairingProfile FromJson(JObject json)
    {
        long issued = json.Value<long?>("issued_at") ?? 0, now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        if (json.Value<int>("v") != 2 || json.Value<string>("provider") != "webdav" || issued < now - 180 || issued > now + 180)
            throw new InvalidDataException("Unsupported or expired cloud profile.");
        var profile = new CloudPairingProfile(json.Value<string>("url")?.Trim() ?? "", json.Value<string>("login")?.Trim() ?? "",
            json.Value<string>("password") ?? "", string.IsNullOrWhiteSpace(json.Value<string>("remote_path")) ? "SDAppVault" : json.Value<string>("remote_path")!,
            json.Value<bool?>("sync_credentials") == true ? json.Value<string>("credentials_key") ?? "" : "",
            json.Value<string>("credentials_remote_path") ?? "", json.Value<string>("pairing_relay_mode") ?? "managed", json.Value<string>("pairing_relay_url") ?? "");
        profile.Validate();
        return profile;
    }
}
