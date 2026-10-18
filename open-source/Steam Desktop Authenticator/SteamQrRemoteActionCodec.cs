using Newtonsoft.Json.Linq;
using System;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Steam_Desktop_Authenticator
{
    internal sealed class SteamQrRemoteRequest
    {
        public string SteamId { get; init; }
        public string QrPayload { get; init; }
    }

    internal sealed class SteamQrRemoteResponse
    {
        public bool Success { get; init; }
        public string SteamId { get; init; }
        public string Error { get; init; }
    }

    internal static class SteamQrRemoteActionCodec
    {
        private static readonly Regex SteamIdPattern = new Regex("^[0-9]{17}$", RegexOptions.CultureInvariant);
        private static readonly Regex QrPattern = new Regex(
            "^https://s\\.team/q/[0-9]+/[0-9]+(?:\\?[A-Za-z0-9._~!$&'()*+,;=:@%/?-]*)?$",
            RegexOptions.CultureInvariant);
        private static readonly string[] Errors =
        {
            "InvalidRequest", "AccountNotGranted", "AccountUnavailable", "ApprovalFailed"
        };

        public static byte[] Request(string steamId, string qrPayload)
        {
            if (!SteamIdPattern.IsMatch(steamId ?? string.Empty))
                throw new ArgumentException("Invalid SteamID for remote QR approval.");
            if (!QrPattern.IsMatch(qrPayload ?? string.Empty))
                throw new ArgumentException("Invalid Steam QR approval payload.");
            return Encoding.UTF8.GetBytes(
                $"{{\"version\":1,\"steamId\":\"{steamId}\",\"qrPayload\":\"{qrPayload}\"}}");
        }

        public static SteamQrRemoteRequest ParseRequest(byte[] payload)
        {
            if (payload == null || payload.Length == 0 || payload.Length > 2048)
                throw new ArgumentException("Invalid remote QR approval request.");
            JObject root = JObject.Parse(Encoding.UTF8.GetString(payload));
            if (root.Value<int>("version") != 1)
                throw new ArgumentException("Unsupported remote QR approval request.");
            SteamQrRemoteRequest value = new SteamQrRemoteRequest
            {
                SteamId = root.Value<string>("steamId"),
                QrPayload = root.Value<string>("qrPayload")
            };
            if (!Request(value.SteamId, value.QrPayload).SequenceEqual(payload))
                throw new ArgumentException("Remote QR approval request is not canonical.");
            return value;
        }

        public static byte[] Response(SteamQrRemoteResponse value)
        {
            if (value == null) throw new ArgumentNullException(nameof(value));
            if (value.Success)
            {
                if (!SteamIdPattern.IsMatch(value.SteamId ?? string.Empty) || value.Error != null)
                    throw new ArgumentException("Invalid successful remote QR approval response.");
            }
            else
            {
                if (value.SteamId != null && !SteamIdPattern.IsMatch(value.SteamId))
                    throw new ArgumentException("Invalid response SteamID.");
                if (!Errors.Contains(value.Error, StringComparer.Ordinal))
                    throw new ArgumentException("Invalid failed remote QR approval response.");
            }
            return Encoding.UTF8.GetBytes(
                "{\"version\":1,\"success\":" + (value.Success ? "true" : "false") +
                ",\"steamId\":" + (value.SteamId == null ? "null" : $"\"{value.SteamId}\"") +
                ",\"error\":" + (value.Error == null ? "null" : $"\"{value.Error}\"") + "}");
        }

        public static SteamQrRemoteResponse ParseResponse(byte[] payload)
        {
            if (payload == null || payload.Length == 0 || payload.Length > 1024)
                throw new ArgumentException("Invalid remote QR approval response.");
            JObject root = JObject.Parse(Encoding.UTF8.GetString(payload));
            if (root.Value<int>("version") != 1)
                throw new ArgumentException("Unsupported remote QR approval response.");
            SteamQrRemoteResponse value = new SteamQrRemoteResponse
            {
                Success = root.Value<bool>("success"),
                SteamId = root["steamId"]?.Type == JTokenType.Null ? null : root.Value<string>("steamId"),
                Error = root["error"]?.Type == JTokenType.Null ? null : root.Value<string>("error")
            };
            if (!Response(value).SequenceEqual(payload))
                throw new ArgumentException("Remote QR approval response is not canonical.");
            return value;
        }
    }
}
