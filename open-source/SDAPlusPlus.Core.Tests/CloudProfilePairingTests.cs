using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json.Linq;
using SDAPlusPlus.Core.Cloud;
using SDAPlusPlus.Core.Devices;

namespace SDAPlusPlus.Core.Tests;
public class CloudProfilePairingTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BothDirectionsMatchMobileCryptoAndFraming(bool send)
    {
        var profile = new CloudPairingProfile("https://cloud.test/", "test", "test-password", "vault");
        using var http = new HttpClient(new EmptyRelay());
        using var session = new CloudProfilePairingSession(http, "https://relay.test", new Signer(), send ? profile : null);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        var running = session.RunAsync(timeout.Token);
        var descriptor = JObject.Parse(Encoding.UTF8.GetString(Decode(session.PairingUri.Split("?p=")[1])));
        Assert.DoesNotContain("test-password", descriptor.ToString());
        Assert.DoesNotContain(session.VerificationCode, descriptor.ToString());
        Assert.Equal(send ? "to-mobile" : "to-desktop", descriptor.Value<string>("direction"));
        using var peer = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        using var desktop = ECDiffieHellman.Create();
        desktop.ImportSubjectPublicKeyInfo(Convert.FromBase64String(descriptor.Value<string>("pk")!), out _);
        byte[] sid = Decode(descriptor.Value<string>("sid")!);
        using var extract = new HMACSHA256(sid);
        using var expand = new HMACSHA256(extract.ComputeHash(peer.DeriveRawSecretAgreement(desktop.PublicKey)));
        byte[] key = expand.ComputeHash(Encoding.UTF8.GetBytes("SDA++ local pairing v2|" + session.VerificationCode).Concat(new byte[] { 1 }).ToArray());
        var request = new JObject { ["v"] = 2, ["sid"] = descriptor["sid"], ["public_key"] = Convert.ToBase64String(peer.ExportSubjectPublicKeyInfo()) };
        using var aes = new AesGcm(key, 16);
        if (!send)
        {
            byte[] plain = Encoding.UTF8.GetBytes(profile.ToJson().ToString()), encrypted = new byte[plain.Length], tag = new byte[16], nonce = RandomNumberGenerator.GetBytes(12);
            aes.Encrypt(nonce, plain, encrypted, tag, sid);
            request["nonce"] = Convert.ToBase64String(nonce);
            request["ciphertext"] = Convert.ToBase64String(encrypted);
            request["tag"] = Convert.ToBase64String(tag);
        }
        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, descriptor.Value<int>("port"), timeout.Token);
        using var stream = client.GetStream();
        byte[] frame = Encoding.UTF8.GetBytes(request.ToString()), header = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(header, frame.Length);
        await stream.WriteAsync(header, timeout.Token);
        await stream.WriteAsync(frame, timeout.Token);
        await stream.ReadExactlyAsync(header, timeout.Token);
        byte[] response = new byte[BinaryPrimitives.ReadInt32BigEndian(header)];
        await stream.ReadExactlyAsync(response, timeout.Token);
        var envelope = JObject.Parse(Encoding.UTF8.GetString(response));
        if (send)
        {
            byte[] encrypted = Convert.FromBase64String(envelope.Value<string>("ciphertext")!), plain = new byte[encrypted.Length];
            aes.Decrypt(Convert.FromBase64String(envelope.Value<string>("nonce")!), encrypted,
                Convert.FromBase64String(envelope.Value<string>("tag")!), plain, sid);
            Assert.Equal(profile, CloudPairingProfile.FromJson(JObject.Parse(Encoding.UTF8.GetString(plain))));
            Assert.Null(await running);
        }
        else
        {
            Assert.True(envelope.Value<bool>("ok"));
            Assert.Equal(profile, await running);
        }
    }
    [Fact]
    public async Task CancellationClosesTransports()
    {
        using var http = new HttpClient(new EmptyRelay());
        using var session = new CloudProfilePairingSession(http, "https://relay.test", new Signer());
        using var cancellation = new CancellationTokenSource();
        var run = session.RunAsync(cancellation.Token);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run.WaitAsync(TimeSpan.FromSeconds(3)));
    }
    [Theory]
    [InlineData("http://cloud.test", 0)]
    [InlineData("https://cloud.test", -1000)]
    [InlineData("https://cloud.test", 1000)]
    public void RejectsInsecureOrStaleProfile(string url, int offset)
    {
        var json = new CloudPairingProfile(url, "test", "test", "vault").ToJson();
        json["issued_at"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + offset;
        Assert.Throws<InvalidDataException>(() => CloudPairingProfile.FromJson(json));
    }
    private static byte[] Decode(string value) => Convert.FromBase64String(value.Replace('-', '+').Replace('_', '/') + new string('=', (4 - value.Length % 4) % 4));
    private sealed class EmptyRelay : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent));
    }
    private sealed class Signer : IDeviceRequestSigner
    {
        public string DeviceId => "test";
        public string PublicKey => "test";
        public string EncryptionPublicKey => "";
        public string EncryptionKeySignature => "";
        public string Protection => "test";
        public string Sign(byte[] payload) => "test";
    }
}
