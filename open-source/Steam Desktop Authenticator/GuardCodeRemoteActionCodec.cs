using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Steam_Desktop_Authenticator
{
    internal sealed class GuardCodeRemoteResponse
    {
        public bool Success { get; init; }
        public string SteamId { get; init; }
        public string Code { get; init; }
        public long? GeneratedAt { get; init; }
        public long? ValidUntil { get; init; }
        public string Error { get; init; }
    }

    internal static class GuardCodeRemoteActionCodec
    {
        private static readonly Regex SteamIdPattern = new Regex("^[0-9]{17}$", RegexOptions.CultureInvariant);
        private static readonly Regex CodePattern = new Regex("^[23456789BCDFGHJKMNPQRTVWXY]{5}$", RegexOptions.CultureInvariant);
        private static readonly HashSet<string> Errors = new HashSet<string>(StringComparer.Ordinal)
        {
            "InvalidRequest", "AccountNotGranted", "AccountUnavailable", "GenerationFailed"
        };

        public static byte[] Request(string steamId)
        {
            if (!SteamIdPattern.IsMatch(steamId ?? string.Empty))
                throw new ArgumentException("Invalid SteamID for remote Guard code request.");
            return Encoding.UTF8.GetBytes($"{{\"version\":1,\"steamId\":\"{steamId}\"}}");
        }

        public static string ParseRequest(byte[] payload)
        {
            if (payload == null || payload.Length == 0 || payload.Length > 1024)
                throw new ArgumentException("Invalid remote Guard code request.");
            JObject root = JObject.Parse(Encoding.UTF8.GetString(payload));
            if (root.Value<int>("version") != 1) throw new ArgumentException("Unsupported remote Guard code request.");
            string steamId = root.Value<string>("steamId");
            if (!Request(steamId).SequenceEqual(payload))
                throw new ArgumentException("Remote Guard code request is not canonical.");
            return steamId;
        }

        public static byte[] Response(GuardCodeRemoteResponse value)
        {
            if (value == null) throw new ArgumentNullException(nameof(value));
            if (value.Success)
            {
                if (!SteamIdPattern.IsMatch(value.SteamId ?? string.Empty) ||
                    !CodePattern.IsMatch(value.Code ?? string.Empty) ||
                    value.GeneratedAt == null || value.ValidUntil == null ||
                    value.ValidUntil <= value.GeneratedAt || value.Error != null)
                    throw new ArgumentException("Invalid successful remote Guard code response.");
            }
            else
            {
                if (value.Code != null || value.GeneratedAt != null || value.ValidUntil != null ||
                    !Errors.Contains(value.Error ?? string.Empty) ||
                    (value.SteamId != null && !SteamIdPattern.IsMatch(value.SteamId)))
                    throw new ArgumentException("Invalid failed remote Guard code response.");
            }
            string json = "{\"version\":1" +
                $",\"success\":{value.Success.ToString().ToLowerInvariant()}" +
                $",\"steamId\":{JsonString(value.SteamId)}" +
                $",\"code\":{JsonString(value.Code)}" +
                $",\"generatedAt\":{value.GeneratedAt?.ToString() ?? "null"}" +
                $",\"validUntil\":{value.ValidUntil?.ToString() ?? "null"}" +
                $",\"error\":{JsonString(value.Error)}}}";
            return Encoding.UTF8.GetBytes(json);
        }

        public static GuardCodeRemoteResponse ParseResponse(byte[] payload)
        {
            if (payload == null || payload.Length == 0 || payload.Length > 2048)
                throw new ArgumentException("Invalid remote Guard code response.");
            JObject root = JObject.Parse(Encoding.UTF8.GetString(payload));
            if (root.Value<int>("version") != 1) throw new ArgumentException("Unsupported remote Guard code response.");
            GuardCodeRemoteResponse value = new GuardCodeRemoteResponse
            {
                Success = root.Value<bool>("success"),
                SteamId = root.Value<string>("steamId"),
                Code = root.Value<string>("code"),
                GeneratedAt = root.Value<long?>("generatedAt"),
                ValidUntil = root.Value<long?>("validUntil"),
                Error = root.Value<string>("error")
            };
            if (!Response(value).SequenceEqual(payload))
                throw new ArgumentException("Remote Guard code response is not canonical.");
            return value;
        }

        private static string JsonString(string value) => value == null ? "null" : $"\"{value}\"";
    }
}
