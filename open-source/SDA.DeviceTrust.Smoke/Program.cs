using System.Reflection;
using System.Text;

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

return firstId == secondId && valid && !tampered && encryptionBindingValid && !encryptionBindingTampered &&
    actionPayload.SequenceEqual(decryptedAction) && actionContextFixtureMatches && actionContextTamperRejected ? 0 : 1;
