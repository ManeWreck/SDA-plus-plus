using System.Reflection;
using System.Text;
using SteamAuth;

Assembly application = Assembly.Load("SDA++");
Type store = application.GetType("Steam_Desktop_Authenticator.DeviceIdentityStore", throwOnError: true)!;
BindingFlags flags = BindingFlags.Public | BindingFlags.Static;
MethodInfo getInfo = store.GetMethod("GetInfo", flags)!;
MethodInfo sign = store.GetMethod("Sign", flags)!;
MethodInfo verify = store.GetMethod("Verify", flags)!;
MethodInfo verifyEncryptionBinding = store.GetMethod("VerifyEncryptionKeyBinding", flags)!;
MethodInfo encryptRemoteAction = store.GetMethod("EncryptRemoteAction", flags)!;
MethodInfo decryptRemoteAction = store.GetMethod("DecryptRemoteAction", flags)!;
Type envelopeCodec = application.GetType(
    "Steam_Desktop_Authenticator.RemoteActionEnvelopeCodec",
    throwOnError: true)!;
MethodInfo associatedData = envelopeCodec.GetMethod(
    "AssociatedData",
    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)!;
Type authorizationType = application.GetType(
    "Steam_Desktop_Authenticator.RemoteActionAuthorization",
    throwOnError: true)!;
Type authorizationCodec = application.GetType(
    "Steam_Desktop_Authenticator.RemoteActionAuthorizationCodec",
    throwOnError: true)!;
object authorization = Activator.CreateInstance(authorizationType)!;
authorizationType.GetProperty("VaultId")!.SetValue(authorization, "ABCDEFGHIJKLMNOPQRSTUV");
authorizationType.GetProperty("InviteId")!.SetValue(authorization, "ZYXWVUTSRQPONMLKJIHGFE");
authorizationType.GetProperty("IssuerDeviceId")!.SetValue(authorization, "abcdefghijklmnopqrstuv");
authorizationType.GetProperty("Role")!.SetValue(authorization, "TrustedOnline");
authorizationType.GetProperty("IssuedAt")!.SetValue(authorization, 1_700_000_000_000L);
authorizationType.GetProperty("ExpiresAt")!.SetValue(authorization, null);
authorizationType.GetProperty("AccountSteamIds")!.SetValue(
    authorization,
    new HashSet<string> { "76561198000000002", "76561198000000001" });
authorizationType.GetProperty("Capabilities")!.SetValue(
    authorization,
    new HashSet<string> { "ApproveSteamQr", "ViewGuardCode" });
byte[] authorizationCanonical = (byte[])authorizationCodec.GetMethod(
    "Canonical",
    BindingFlags.Public | BindingFlags.Static)!.Invoke(null, new[] { authorization })!;
bool authorizationFixtureMatches = Encoding.UTF8.GetString(authorizationCanonical) ==
    "{\"version\":1,\"vaultId\":\"ABCDEFGHIJKLMNOPQRSTUV\"," +
    "\"inviteId\":\"ZYXWVUTSRQPONMLKJIHGFE\"," +
    "\"issuerDeviceId\":\"abcdefghijklmnopqrstuv\"," +
    "\"role\":\"TrustedOnline\",\"issuedAt\":1700000000000,\"expiresAt\":null," +
    "\"accountSteamIds\":[\"76561198000000001\",\"76561198000000002\"]," +
    "\"capabilities\":[\"ApproveSteamQr\",\"ViewGuardCode\"]}";
Type guardResponseType = application.GetType(
    "Steam_Desktop_Authenticator.GuardCodeRemoteResponse",
    throwOnError: true)!;
Type guardCodec = application.GetType(
    "Steam_Desktop_Authenticator.GuardCodeRemoteActionCodec",
    throwOnError: true)!;
byte[] guardRequest = (byte[])guardCodec.GetMethod(
    "Request",
    BindingFlags.Public | BindingFlags.Static)!.Invoke(null, new object[] { "76561198000000001" })!;
object guardResponse = Activator.CreateInstance(guardResponseType)!;
guardResponseType.GetProperty("Success")!.SetValue(guardResponse, true);
guardResponseType.GetProperty("SteamId")!.SetValue(guardResponse, "76561198000000001");
guardResponseType.GetProperty("Code")!.SetValue(guardResponse, "2BCDF");
guardResponseType.GetProperty("GeneratedAt")!.SetValue(guardResponse, 1_700_000_000_000L);
guardResponseType.GetProperty("ValidUntil")!.SetValue(guardResponse, 1_700_000_010_000L);
byte[] guardResponsePayload = (byte[])guardCodec.GetMethod(
    "Response",
    BindingFlags.Public | BindingFlags.Static)!.Invoke(null, new[] { guardResponse })!;
bool guardCodeFixtureMatches =
    Encoding.UTF8.GetString(guardRequest) == "{\"version\":1,\"steamId\":\"76561198000000001\"}" &&
    (string)guardCodec.GetMethod("ParseRequest", BindingFlags.Public | BindingFlags.Static)!
        .Invoke(null, new object[] { guardRequest })! == "76561198000000001" &&
    guardCodec.GetMethod("ParseResponse", BindingFlags.Public | BindingFlags.Static)!
        .Invoke(null, new object[] { guardResponsePayload }) != null &&
    Encoding.UTF8.GetString(guardResponsePayload) ==
        "{\"version\":1,\"success\":true,\"steamId\":\"76561198000000001\"," +
        "\"code\":\"2BCDF\",\"generatedAt\":1700000000000,\"validUntil\":1700000010000,\"error\":null}";
Type qrRequestType = application.GetType(
    "Steam_Desktop_Authenticator.SteamQrRemoteRequest",
    throwOnError: true)!;
Type qrResponseType = application.GetType(
    "Steam_Desktop_Authenticator.SteamQrRemoteResponse",
    throwOnError: true)!;
Type qrCodec = application.GetType(
    "Steam_Desktop_Authenticator.SteamQrRemoteActionCodec",
    throwOnError: true)!;
const string qrUrl = "https://s.team/q/1/1234567890123456789";
byte[] qrRequest = (byte[])qrCodec.GetMethod(
    "Request",
    BindingFlags.Public | BindingFlags.Static)!.Invoke(
        null,
        new object[] { "76561198000000001", qrUrl })!;
object parsedQrRequest = qrCodec.GetMethod(
    "ParseRequest",
    BindingFlags.Public | BindingFlags.Static)!.Invoke(null, new object[] { qrRequest })!;
object qrResponse = Activator.CreateInstance(qrResponseType)!;
qrResponseType.GetProperty("Success")!.SetValue(qrResponse, true);
qrResponseType.GetProperty("SteamId")!.SetValue(qrResponse, "76561198000000001");
byte[] qrResponsePayload = (byte[])qrCodec.GetMethod(
    "Response",
    BindingFlags.Public | BindingFlags.Static)!.Invoke(null, new[] { qrResponse })!;
bool steamQrFixtureMatches =
    Encoding.UTF8.GetString(qrRequest) ==
        "{\"version\":1,\"steamId\":\"76561198000000001\"," +
        "\"qrPayload\":\"https://s.team/q/1/1234567890123456789\"}" &&
    (string)qrRequestType.GetProperty("SteamId")!.GetValue(parsedQrRequest)! == "76561198000000001" &&
    (string)qrRequestType.GetProperty("QrPayload")!.GetValue(parsedQrRequest)! == qrUrl &&
    qrCodec.GetMethod("ParseResponse", BindingFlags.Public | BindingFlags.Static)!
        .Invoke(null, new object[] { qrResponsePayload }) != null &&
    Encoding.UTF8.GetString(qrResponsePayload) ==
        "{\"version\":1,\"success\":true,\"steamId\":\"76561198000000001\",\"error\":null}";

Type registrySettingsType = application.GetType(
    "Steam_Desktop_Authenticator.DesktopRegistrySettings",
    throwOnError: true)!;
Type deviceAccessType = application.GetType(
    "Steam_Desktop_Authenticator.DesktopDeviceAccess",
    throwOnError: true)!;
object trustedSettings = Activator.CreateInstance(registrySettingsType)!;
registrySettingsType.GetProperty("Role")!.SetValue(trustedSettings, "TrustedOnline");
registrySettingsType.GetProperty("GrantedAccountSteamIds")!.SetValue(
    trustedSettings,
    new HashSet<string> { "76561198000000001", "76561198000000002" });
SteamGuardAccount localGrantedAccount = new SteamGuardAccount
{
    AccountName = "local-granted",
    SharedSecret = "local-secret-fixture",
    Session = new SessionData
    {
        SteamID = 76561198000000001,
        AccessToken = "local-access-token-fixture"
    }
};
SteamGuardAccount localUngrantedAccount = new SteamGuardAccount
{
    AccountName = "local-ungranted",
    Session = new SessionData { SteamID = 76561198000000003 }
};
HashSet<ulong> remoteSteamIds = new HashSet<ulong>();
SteamGuardAccount[] visibleAccounts = (SteamGuardAccount[])deviceAccessType.GetMethod(
    "BuildVisibleAccountsForSettings",
    BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(
        null,
        new object[]
        {
            new[] { localGrantedAccount, localUngrantedAccount },
            remoteSteamIds,
            (Func<ulong, string>)(steamId => $"remote-{steamId}"),
            trustedSettings
        })!;
SteamGuardAccount remotePlaceholder = visibleAccounts.Single(account => account.Session.SteamID == 76561198000000002);
bool remotePlaceholderSafe =
    visibleAccounts.Length == 2 &&
    ReferenceEquals(visibleAccounts.Single(account => account.Session.SteamID == 76561198000000001), localGrantedAccount) &&
    remoteSteamIds.SetEquals(new[] { 76561198000000002UL }) &&
    remotePlaceholder.SharedSecret == null &&
    remotePlaceholder.IdentitySecret == null &&
    remotePlaceholder.Session.AccessToken == null &&
    remotePlaceholder.Session.RefreshToken == null &&
    visibleAccounts.All(account => account.Session.SteamID != 76561198000000003);

object first = getInfo.Invoke(null, null)!;
object second = getInfo.Invoke(null, null)!;
Type infoType = first.GetType();
string firstId = (string)infoType.GetProperty("DeviceId")!.GetValue(first)!;
string secondId = (string)infoType.GetProperty("DeviceId")!.GetValue(second)!;
string publicKey = (string)infoType.GetProperty("PublicKeyDerBase64Url")!.GetValue(first)!;
string protection = (string)infoType.GetProperty("Protection")!.GetValue(first)!;
bool hardwareBacked = (bool)infoType.GetProperty("HardwareBacked")!.GetValue(first)!;
string encryptionPublicKey = (string)infoType.GetProperty("EncryptionPublicKeyDerBase64Url")!.GetValue(first)!;
string encryptionKeySignature = (string)infoType.GetProperty("EncryptionKeySignatureBase64Url")!.GetValue(first)!;

byte[] payload = Encoding.UTF8.GetBytes("SDA++ device identity smoke test");
string signature = (string)sign.Invoke(null, new object[] { payload })!;
bool valid = (bool)verify.Invoke(null, new object[] { publicKey, payload, signature })!;
bool tampered = (bool)verify.Invoke(null, new object[] { publicKey, payload.Concat(new byte[] { 1 }).ToArray(), signature })!;
bool encryptionBindingValid = (bool)verifyEncryptionBinding.Invoke(
    null,
    new object[] { firstId, publicKey, encryptionPublicKey, encryptionKeySignature })!;
bool encryptionBindingTampered = (bool)verifyEncryptionBinding.Invoke(
    null,
    new object[] { firstId, publicKey, encryptionPublicKey + "A", encryptionKeySignature })!;
string vaultId = "ABCDEFGHIJKLMNOPQRSTUV";
string actionId = "123e4567-e89b-42d3-a456-426614174000";
byte[] actionPayload = Encoding.UTF8.GetBytes("opaque remote action payload");
string actionEnvelope = (string)encryptRemoteAction.Invoke(
    null,
    new object[] { encryptionPublicKey, actionPayload, vaultId, actionId, "request" })!;
byte[] decryptedAction = (byte[])decryptRemoteAction.Invoke(
    null,
    new object[] { actionEnvelope, vaultId, actionId, "request" })!;
string actionContext = Encoding.UTF8.GetString((byte[])associatedData.Invoke(
    null,
    new object[] { vaultId, actionId, "request" })!);
bool actionContextFixtureMatches = actionContext ==
    "sdapp-remote-action/v1\n" +
    "vaultId=22:ABCDEFGHIJKLMNOPQRSTUV\n" +
    "actionId=36:123e4567-e89b-42d3-a456-426614174000\n" +
    "direction=7:request\n";
bool actionContextTamperRejected;
try
{
    decryptRemoteAction.Invoke(null, new object[] { actionEnvelope, vaultId, actionId, "response" });
    actionContextTamperRejected = false;
}
catch (TargetInvocationException)
{
    actionContextTamperRejected = true;
}

Console.WriteLine($"StableDeviceId={firstId == secondId}");
Console.WriteLine($"DeviceId={firstId}");
Console.WriteLine($"Protection={protection}");
Console.WriteLine($"HardwareBacked={hardwareBacked}");
Console.WriteLine($"SignatureVerified={valid}");
Console.WriteLine($"TamperedRejected={!tampered}");
Console.WriteLine($"EncryptionBindingVerified={encryptionBindingValid}");
Console.WriteLine($"EncryptionBindingTamperRejected={!encryptionBindingTampered}");
Console.WriteLine($"RemoteActionRoundTrip={actionPayload.SequenceEqual(decryptedAction)}");
Console.WriteLine($"RemoteActionContextFixtureMatches={actionContextFixtureMatches}");
Console.WriteLine($"RemoteActionContextTamperRejected={actionContextTamperRejected}");
Console.WriteLine($"RemoteActionAuthorizationFixtureMatches={authorizationFixtureMatches}");
Console.WriteLine($"GuardCodeRemotePayloadFixtureMatches={guardCodeFixtureMatches}");
Console.WriteLine($"SteamQrRemotePayloadFixtureMatches={steamQrFixtureMatches}");
Console.WriteLine($"RemoteAccountPlaceholderSafe={remotePlaceholderSafe}");

return firstId == secondId && valid && !tampered && encryptionBindingValid && !encryptionBindingTampered &&
    actionPayload.SequenceEqual(decryptedAction) && actionContextFixtureMatches && actionContextTamperRejected &&
    authorizationFixtureMatches && guardCodeFixtureMatches && steamQrFixtureMatches && remotePlaceholderSafe ? 0 : 1;
