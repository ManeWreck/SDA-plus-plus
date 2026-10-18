using System.Net;
using System.Text;
using System.Security.Cryptography;
using Newtonsoft.Json.Linq;
using SDAPlusPlus.Core.Devices;

namespace SDAPlusPlus.Core.Tests.Devices;

public sealed class DeviceRegistryClientTests
{
    [Fact]
    public async Task PendingCommandsRejectLegacyUnsignedResponse()
    {
        string id = Guid.NewGuid().ToString("D");
        var handler = new CapturingHandler(HttpStatusCode.OK, new JObject { ["commands"] = new JArray(new JObject { ["commandId"] = id, ["type"] = "Lock", ["expiresAt"] = 1 }) }.ToString());
        var client = new DeviceRegistryClient(new HttpClient(handler), new FakeSigner());
        var result = await client.PendingCommandsAsync(ConfiguredSettings());
        Assert.False(result.Success);
        Assert.Empty(result.Commands);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"commands\":{}}")]
    [InlineData("{\"commands\":[{}]}")]
    [InlineData("{\"commands\":[{\"commandId\":\"bad\",\"type\":\"Lock\",\"expiresAt\":1}]}")]
    public async Task InvalidCommandListDoesNotLookLikeAnEmptyQueue(string json)
    {
        var client = new DeviceRegistryClient(new HttpClient(new CapturingHandler(HttpStatusCode.OK, json)), new FakeSigner());
        Assert.False((await client.PendingCommandsAsync(ConfiguredSettings())).Success);
    }

    [Theory]
    [InlineData(DeviceExecutionState.Started, "device-test", false)]
    [InlineData(DeviceExecutionState.OutcomeUnknown, "device-test", false)]
    [InlineData(DeviceExecutionState.Expired, "device-test", false)]
    [InlineData(DeviceExecutionState.Completed, "other-device", false)]
    [InlineData(DeviceExecutionState.Completed, "device-test", false)]
    [InlineData(DeviceExecutionState.Failed, "device-test", false)]
    public async Task AcknowledgmentOnlySendsKnownFinalResultForThisDevice(DeviceExecutionState state, string device, bool expected)
    {
        var handler = new CapturingHandler(HttpStatusCode.OK, "{}");
        var client = new DeviceRegistryClient(new HttpClient(handler), new FakeSigner());
        var command = new IncomingDeviceCommand("vault-1", device, Guid.NewGuid().ToString("D"), "Lock", long.MaxValue);
        var result = await client.AcknowledgeCommandAsync(ConfiguredSettings(), new(command, state));
        Assert.Equal(expected, result.Success);
        if (expected)
        {
            Assert.Equal(HttpMethod.Post, handler.Method);
            Assert.Equal(state.ToString(), JObject.Parse(handler.Body)["status"]!.Value<string>());
        }
        else Assert.Null(handler.Method);
    }

    [Theory]
    [InlineData("v=1", "v=2")]
    [InlineData("v=1&", "")]
    [InlineData("relay.example", "relay.example%2Funexpected")]
    [InlineData("relay.example", "user%40relay.example")]
    [InlineData("https%3A", "http%3A")]
    [InlineData("token=AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA", "token=short")]
    [InlineData("token=AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA", "token=AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAB")]
    [InlineData("exp=9999999999999", "exp=1")]
    [InlineData("device-enroll?", "device-enroll/path?")]
    public async Task UnsupportedInvitationIsRejectedBeforeNetwork(string original, string replacement)
    {
        CapturingHandler handler = new(HttpStatusCode.OK, "{}");
        DeviceRegistryClient client = new(new HttpClient(handler), new FakeSigner());
        string qr = "sdapp://device-enroll?v=1&relay=https%3A%2F%2Frelay.example&vault=AAAAAAAAAAAAAAAAAAAAAA&invite=BBBBBBBBBBBBBBBBBBBBBB&token=AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA&exp=9999999999999";
        var result = await client.ClaimInvitationAsync(qr.Replace(original, replacement), "123456", "Test");
        Assert.False(result.Success);
        Assert.Null(handler.Method);
    }

    [Fact]
    public async Task OversizedInvitationIsRejectedBeforeNetwork()
    {
        CapturingHandler handler = new(HttpStatusCode.OK, "{}");
        DeviceRegistryClient client = new(new HttpClient(handler), new FakeSigner());
        var result = await client.ClaimInvitationAsync("sdapp://device-enroll?" + new string('a', 8192), "123456", "Test");
        Assert.False(result.Success);
        Assert.Null(handler.Method);
    }

    [Theory]
    [InlineData("TrustedOnline", false)]
    [InlineData("TrustedOffline", false)]
    [InlineData("Temporary", false)]
    [InlineData("Admin", true)]
    [InlineData("", false)]
    public async Task InvitationRequiresActiveAdminBeforeDispatch(string role, bool expired)
    {
        CapturingHandler handler = new(HttpStatusCode.OK, "{}");
        DeviceRegistryClient client = new(new HttpClient(handler), new FakeSigner());
        var settings = ConfiguredSettings();
        settings.Role = role;
        settings.ExpiresAt = expired ? 1 : null;
        var result = await client.CreateInvitationAsync(settings, "Admin", [], []);
        Assert.False(result.Success);
        Assert.Null(handler.Method);
    }

    [Theory]
    [InlineData("&vault=BBBBBBBBBBBBBBBBBBBBBB")]
    [InlineData("&%76ault=BBBBBBBBBBBBBBBBBBBBBB")]
    [InlineData("&broken")]
    public async Task MalformedInvitationFailsWithoutRequest(string suffix)
    {
        CapturingHandler handler = new(HttpStatusCode.OK, "{}");
        DeviceRegistryClient client = new(new HttpClient(handler), new FakeSigner());
        string qr = "sdapp://device-enroll?v=1&relay=https%3A%2F%2Frelay.example&vault=AAAAAAAAAAAAAAAAAAAAAA&invite=BBBBBBBBBBBBBBBBBBBBBB&token=test&exp=9999999999999";
        var result = await client.ClaimInvitationAsync(qr + suffix, "123456", "Test");
        Assert.False(result.Success);
        Assert.Null(handler.Method);
    }

    [Theory]
    [InlineData("TrustedOnline", false, "phone-1")]
    [InlineData("Admin", true, "phone-1")]
    [InlineData("Admin", false, "device-test")]
    public async Task InvalidAuthorityOrSelfTargetNeverSendsDestructiveRequest(string role, bool expired, string target)
    {
        CapturingHandler handler = new(HttpStatusCode.OK, "{}");
        DeviceRegistryClient client = new(new HttpClient(handler), new FakeSigner());
        var settings = ConfiguredSettings();
        settings.Role = role;
        settings.ExpiresAt = expired ? 1 : null;
        Assert.False((await client.RevokeAsync(settings, target)).Success);
        Assert.False((await client.IssueCommandAsync(settings, target, "WipeLocalData")).Success);
        Assert.Null(handler.Method);
    }

    [Fact]
    public async Task ListDevices_SendsSignedCompatibleRequest()
    {
        CapturingHandler handler = new(HttpStatusCode.OK, """
            {"devices":[{"deviceId":"phone-1","displayName":"Phone","role":"TrustedOnline","lastSeenAt":10}]}
            """);
        FakeSigner signer = new();
        DeviceRegistryClient client = new(new HttpClient(handler), signer);
        DeviceRegistrySettings settings = new()
        {
            VaultId = "vault-1",
            RelayUrl = "https://relay.example",
            Role = "Admin"
        };

        DeviceListResult result = await client.ListDevicesAsync(settings);

        Assert.True(result.Success);
        RegistryDevice device = Assert.Single(result.Devices);
        Assert.Equal("Phone", device.DisplayName);
        Assert.Equal("TrustedOnline", device.Role);
        Assert.Equal("device-test", handler.DeviceId);
        Assert.False(string.IsNullOrWhiteSpace(handler.Signature));
        Assert.StartsWith("sdapp-device-request/v1\n", Encoding.UTF8.GetString(signer.LastPayload));
    }

    [Fact]
    public void CanonicalRequest_MatchesLegacyFormat()
    {
        byte[] canonical = DeviceRegistryClient.CreateCanonicalRequest(
            "GET",
            "/v1/trust/vault/devices",
            123,
            "abc",
            []);

        string text = Encoding.UTF8.GetString(canonical);
        Assert.Contains("method=3:GET\n", text);
        Assert.Contains("path=23:/v1/trust/vault/devices\n", text);
        Assert.Contains("timestamp=3:123\n", text);
        Assert.Contains("nonce=3:abc\n", text);
        Assert.Contains("bodyHash=43:47DEQpj8HBSa-_TImW-5JCeuQeRkm5NMpJWZG3hSuFU\n", text);
    }

    [Fact]
    public async Task ListAudit_ReadsSecurityEvents()
    {
        CapturingHandler handler = new(HttpStatusCode.OK, """
            {"events":[{"eventId":"event-1","action":"device.revoked","actorDeviceId":"admin","targetDeviceId":"phone","occurredAt":1000,"country":"MD"}]}
            """);
        DeviceRegistryClient client = new(new HttpClient(handler), new FakeSigner());
        DeviceRegistrySettings settings = new()
        {
            VaultId = "vault-1",
            RelayUrl = "https://relay.example",
            Role = "Admin"
        };

        DeviceAuditResult result = await client.ListAuditAsync(settings);

        Assert.True(result.Success);
        RegistryAuditEvent item = Assert.Single(result.Events);
        Assert.Equal("device.revoked", item.Action);
        Assert.Equal("phone", item.TargetDeviceId);
    }

    [Fact]
    public async Task IssueCommand_RejectsTargetWithoutLocalSignedGrant()
    {
        CapturingHandler handler = new(HttpStatusCode.OK, "{}");
        DeviceRegistryClient client = new(new HttpClient(handler), new FakeSigner());
        DeviceRegistrySettings settings = ConfiguredSettings();

        DeviceCommandResult result = await client.IssueCommandAsync(settings, "phone/1", "Lock");

        Assert.False(result.Success);
        Assert.Null(handler.Method);
    }

    [Fact]
    public async Task Revoke_SendsSignedDelete()
    {
        CapturingHandler handler = new(HttpStatusCode.OK, "{}");
        DeviceRegistryClient client = new(new HttpClient(handler), new FakeSigner());

        DeviceCommandResult result = await client.RevokeAsync(ConfiguredSettings(), "phone-1");

        Assert.True(result.Success);
        Assert.Equal(HttpMethod.Delete, handler.Method);
        Assert.EndsWith("/devices/phone-1", handler.Path);
    }

    [Fact]
    public async Task CreateInvitation_RejectsLegacyTokenDerivedEncryption()
    {
        CapturingHandler handler = new(HttpStatusCode.OK, "{}");
        DeviceRegistryClient client = new(new HttpClient(handler), new InvitationSigner());
        DeviceRegistrySettings settings = new()
        {
            VaultId = "AAAAAAAAAAAAAAAAAAAAAA",
            RelayUrl = "https://relay.example",
            Role = "Admin"
        };

        DeviceInvitationResult result = await client.CreateInvitationAsync(
            settings,
            "TrustedOnline",
            [new DeviceInvitationAccount("76561198000000000")],
            ["ViewGuardCode", "ApproveSteamQr"]);

        Assert.False(result.Success);
        Assert.Null(handler.Method);
    }

    [Fact]
    public async Task ClaimInvitation_VerifiesAdminGrantAndCreatesRestrictedRegistry()
    {
        using CryptoSigner admin = new();
        using CryptoSigner desktop = new();
        PairingHandler handler = new(desktop.DeviceId);
        DeviceRegistryClient adminClient = new(new HttpClient(handler), admin, _ => { });
        DeviceRegistrySettings adminSettings = new()
        {
            VaultId = "AAAAAAAAAAAAAAAAAAAAAA",
            RelayUrl = "https://relay.example",
            Role = "Admin"
        };
        DeviceInvitationResult invitation = await adminClient.CreateBoundInvitationAsync(
            adminSettings,
            EndToEndTrust.RecipientCode(desktop),
            "TrustedOnline",
            [new DeviceInvitationAccount("76561198000000000")],
            ["ViewGuardCode"]);

        DeviceRegistryClient desktopClient = new(new HttpClient(handler), desktop);
        DeviceClaimResult claim = await desktopClient.ClaimInvitationAsync(
            invitation.QrPayload!, invitation.VerificationCode!, "Desktop");

        Assert.True(claim.Success, claim.Detail);
        Assert.Equal("TrustedOnline", claim.Settings!.Role);
        Assert.Equal(desktop.DeviceId, handler.ClaimDeviceId);
        Assert.Contains("76561198000000000", claim.Settings.GrantedAccountSteamIds);
        Assert.Contains("ViewGuardCode", claim.Settings.Capabilities);
        Assert.Empty(claim.Accounts!);
    }

    private static DeviceRegistrySettings ConfiguredSettings() => new()
    {
        VaultId = "vault-1",
        RelayUrl = "https://relay.example",
        Role = "Admin"
    };

    private sealed class FakeSigner : IDeviceRequestSigner
    {
        public string DeviceId => "device-test";
        public string PublicKey => "public-test";
        public string EncryptionPublicKey => "encryption-test";
        public string EncryptionKeySignature => "encryption-signature-test";
        public string Protection => "test";
        public byte[] LastPayload { get; private set; } = [];

        public string Sign(byte[] payload)
        {
            LastPayload = payload;
            return "signature-test";
        }
    }

    private sealed class InvitationSigner : IDeviceRequestSigner
    {
        public string DeviceId => "BBBBBBBBBBBBBBBBBBBBBB";
        public string PublicKey => "public";
        public string EncryptionPublicKey => "encryption";
        public string EncryptionKeySignature => "binding";
        public string Protection => "test";
        public string Sign(byte[] payload) => "signature";
    }

    private sealed class CryptoSigner : IDeviceRequestSigner, IDisposable
    {
        private readonly ECDsa _signer = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        private readonly RSA _encryption = RSA.Create(2048);

        public CryptoSigner()
        {
            byte[] publicKey = _signer.ExportSubjectPublicKeyInfo();
            PublicKey = Encode(publicKey);
            DeviceId = Encode(SHA256.HashData(publicKey)[..16]);
            EncryptionPublicKey = Encode(_encryption.ExportSubjectPublicKeyInfo());
            StringBuilder binding = new("sdapp-device-encryption-key/v1\n");
            Add(binding, "deviceId", DeviceId);
            Add(binding, "publicKey", EncryptionPublicKey);
            EncryptionKeySignature = Sign(Encoding.UTF8.GetBytes(binding.ToString()));
        }

        public string DeviceId { get; }
        public string PublicKey { get; }
        public string EncryptionPublicKey { get; }
        public string EncryptionKeySignature { get; }
        public string Protection => "test";
        public string Sign(byte[] payload) => Encode(_signer.SignData(payload, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence));
        public byte[] UnwrapTrustKey(byte[] wrapped) => _encryption.Decrypt(wrapped, RSAEncryptionPadding.OaepSHA256);
        public void Dispose() { _signer.Dispose(); _encryption.Dispose(); }
        private static string Encode(byte[] value) => Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        private static void Add(StringBuilder value, string name, string item) =>
            value.Append(name).Append('=').Append(Encoding.UTF8.GetByteCount(item)).Append(':').Append(item).Append('\n');
    }

    private sealed class PairingHandler(string claimDeviceId) : HttpMessageHandler
    {
        private JObject? _invitation;
        public string? ClaimDeviceId { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            string body = request.Content is null ? "{}" : await request.Content.ReadAsStringAsync(cancellationToken);
            if (request.Method == HttpMethod.Post)
            {
                _invitation = JObject.Parse(body);
                return Json("{}");
            }

            ClaimDeviceId = request.Headers.GetValues("x-sdapp-device-id").Single();
            Assert.Equal(claimDeviceId, ClaimDeviceId);
            Assert.True(request.Headers.Contains("x-sdapp-public-key"));
            Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
            return Json(new JObject
            {
                ["grantEnvelope"] = _invitation!["grantEnvelope"],
                ["device"] = new JObject
                {
                    ["deviceId"] = claimDeviceId,
                    ["displayName"] = "Desktop",
                    ["role"] = "TrustedOnline",
                    ["grantFingerprint"] = _invitation["grantFingerprint"]
                }
            }.ToString());
        }

        private static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
    }

    private sealed class CapturingHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        public string? DeviceId { get; private set; }
        public string? Signature { get; private set; }
        public HttpMethod? Method { get; private set; }
        public string? Path { get; private set; }
        public string Body { get; private set; } = string.Empty;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            DeviceId = request.Headers.GetValues("x-sdapp-device-id").Single();
            Signature = request.Headers.GetValues("x-sdapp-signature").Single();
            Method = request.Method;
            Path = request.RequestUri?.AbsolutePath;
            Body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(status)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            };
        }
    }
}
