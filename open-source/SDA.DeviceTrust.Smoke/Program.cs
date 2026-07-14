using System.Reflection;
using System.Text;

Assembly application = Assembly.Load("SDA++");
Type store = application.GetType("Steam_Desktop_Authenticator.DeviceIdentityStore", throwOnError: true)!;
BindingFlags flags = BindingFlags.Public | BindingFlags.Static;
MethodInfo getInfo = store.GetMethod("GetInfo", flags)!;
MethodInfo sign = store.GetMethod("Sign", flags)!;
MethodInfo verify = store.GetMethod("Verify", flags)!;

object first = getInfo.Invoke(null, null)!;
object second = getInfo.Invoke(null, null)!;
Type infoType = first.GetType();
string firstId = (string)infoType.GetProperty("DeviceId")!.GetValue(first)!;
string secondId = (string)infoType.GetProperty("DeviceId")!.GetValue(second)!;
string publicKey = (string)infoType.GetProperty("PublicKeyDerBase64Url")!.GetValue(first)!;
string protection = (string)infoType.GetProperty("Protection")!.GetValue(first)!;
bool hardwareBacked = (bool)infoType.GetProperty("HardwareBacked")!.GetValue(first)!;

byte[] payload = Encoding.UTF8.GetBytes("SDA++ device identity smoke test");
string signature = (string)sign.Invoke(null, new object[] { payload })!;
bool valid = (bool)verify.Invoke(null, new object[] { publicKey, payload, signature })!;
bool tampered = (bool)verify.Invoke(null, new object[] { publicKey, payload.Concat(new byte[] { 1 }).ToArray(), signature })!;

Console.WriteLine($"StableDeviceId={firstId == secondId}");
Console.WriteLine($"DeviceId={firstId}");
Console.WriteLine($"Protection={protection}");
Console.WriteLine($"HardwareBacked={hardwareBacked}");
Console.WriteLine($"SignatureVerified={valid}");
Console.WriteLine($"TamperedRejected={!tampered}");

return firstId == secondId && valid && !tampered ? 0 : 1;
