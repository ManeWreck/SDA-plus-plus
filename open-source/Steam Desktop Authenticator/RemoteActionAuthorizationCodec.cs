using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Steam_Desktop_Authenticator
{
    internal sealed class RemoteActionAuthorization
    {
        public string VaultId { get; init; }
        public string InviteId { get; init; }
        public string IssuerDeviceId { get; init; }
        public string Role { get; init; }
        public long IssuedAt { get; init; }
        public long? ExpiresAt { get; init; }
        public HashSet<string> AccountSteamIds { get; init; } = new HashSet<string>(StringComparer.Ordinal);
        public HashSet<string> Capabilities { get; init; } = new HashSet<string>(StringComparer.Ordinal);
    }

    internal static class RemoteActionAuthorizationCodec
    {
        private static readonly Regex IdPattern = new Regex("^[A-Za-z0-9_-]{22}$", RegexOptions.CultureInvariant);
        private static readonly Regex SteamIdPattern = new Regex("^[0-9]{17}$", RegexOptions.CultureInvariant);
        private static readonly HashSet<string> Roles = new HashSet<string>(StringComparer.Ordinal)
        {
            "Admin", "TrustedOnline", "TrustedOffline", "Temporary"
        };
        private static readonly HashSet<string> AllowedCapabilities = new HashSet<string>(
            Enum.GetNames(typeof(DeviceCapability)),
            StringComparer.Ordinal);

        public static byte[] Canonical(RemoteActionAuthorization value)
        {
            if (value == null || !IdPattern.IsMatch(value.VaultId ?? string.Empty) ||
                !IdPattern.IsMatch(value.InviteId ?? string.Empty) ||
                !IdPattern.IsMatch(value.IssuerDeviceId ?? string.Empty))
                throw new ArgumentException("Invalid remote action authorization identity.");
            if (!Roles.Contains(value.Role ?? string.Empty))
                throw new ArgumentException("Invalid remote action authorization role.");
            if (value.IssuedAt <= 0 || (value.ExpiresAt != null && value.ExpiresAt <= value.IssuedAt))
                throw new ArgumentException("Invalid remote action authorization lifetime.");
            if (value.AccountSteamIds.Count > 64 || value.AccountSteamIds.Any(id => !SteamIdPattern.IsMatch(id)))
                throw new ArgumentException("Invalid remote action account grant.");
            if (value.Capabilities.Any(capability => !AllowedCapabilities.Contains(capability)))
                throw new ArgumentException("Invalid remote action capability.");

            string accountIds = string.Join(",", value.AccountSteamIds.OrderBy(id => id, StringComparer.Ordinal).Select(id => $"\"{id}\""));
            string capabilities = string.Join(",", value.Capabilities.OrderBy(capability => capability, StringComparer.Ordinal)
                .Select(capability => $"\"{capability}\""));
            string json = "{\"version\":1" +
                $",\"vaultId\":\"{value.VaultId}\"" +
                $",\"inviteId\":\"{value.InviteId}\"" +
                $",\"issuerDeviceId\":\"{value.IssuerDeviceId}\"" +
                $",\"role\":\"{value.Role}\"" +
                $",\"issuedAt\":{value.IssuedAt}" +
                $",\"expiresAt\":{(value.ExpiresAt?.ToString() ?? "null")}" +
                $",\"accountSteamIds\":[{accountIds}]" +
                $",\"capabilities\":[{capabilities}]}}";
            return Encoding.UTF8.GetBytes(json);
        }

        public static RemoteActionAuthorization Parse(byte[] canonicalBytes)
        {
            if (canonicalBytes == null || canonicalBytes.Length == 0 || canonicalBytes.Length > 16 * 1024)
                throw new ArgumentException("Invalid remote action authorization.");
            JObject root = JObject.Parse(Encoding.UTF8.GetString(canonicalBytes));
            if (root.Value<int>("version") != 1) throw new ArgumentException("Unsupported remote action authorization.");
            RemoteActionAuthorization value = new RemoteActionAuthorization
            {
                VaultId = root.Value<string>("vaultId"),
                InviteId = root.Value<string>("inviteId"),
                IssuerDeviceId = root.Value<string>("issuerDeviceId"),
                Role = root.Value<string>("role"),
                IssuedAt = root.Value<long>("issuedAt"),
                ExpiresAt = root.Value<long?>("expiresAt"),
                AccountSteamIds = root["accountSteamIds"]?.Values<string>().ToHashSet(StringComparer.Ordinal)
                    ?? new HashSet<string>(StringComparer.Ordinal),
                Capabilities = root["capabilities"]?.Values<string>().ToHashSet(StringComparer.Ordinal)
                    ?? new HashSet<string>(StringComparer.Ordinal)
            };
            if (!Canonical(value).SequenceEqual(canonicalBytes))
                throw new ArgumentException("Remote action authorization is not canonical.");
            return value;
        }

        public static string Fingerprint(byte[] canonicalBytes) =>
            Convert.ToBase64String(SHA256.HashData(canonicalBytes)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }
}
