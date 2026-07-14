using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace Steam_Desktop_Authenticator
{
    internal enum DeviceRole
    {
        Admin,
        TrustedOnline,
        TrustedOffline,
        Temporary
    }

    internal enum DeviceCapability
    {
        ViewGuardCode,
        ApproveSteamQr,
        ViewConfirmations,
        ApproveConfirmations,
        TerminateSessions,
        IssueTemporaryAccess,
        SyncVault,
        ExportVault,
        ManageDevices
    }

    internal sealed class AccountGrantDocument
    {
        public string SteamId { get; init; }
        public HashSet<DeviceCapability> Capabilities { get; init; } = new HashSet<DeviceCapability>();
        public long IssuedAtEpochSeconds { get; init; }
        public long ExpiresAtEpochSeconds { get; init; }
        public int? MaximumUses { get; init; }
        public bool RequireStepUpAuthentication { get; init; } = true;

        public bool Permits(DeviceCapability capability, long nowEpochSeconds, int uses = 0) =>
            Capabilities.Contains(capability) && nowEpochSeconds >= IssuedAtEpochSeconds &&
            nowEpochSeconds < ExpiresAtEpochSeconds && (MaximumUses == null || uses < MaximumUses);
    }

    internal sealed class DeviceLeaseDocument
    {
        public string DeviceId { get; init; }
        public DeviceRole Role { get; init; }
        public long IssuedAtEpochSeconds { get; init; }
        public long ExpiresAtEpochSeconds { get; init; }
        public List<AccountGrantDocument> AccountGrants { get; init; } = new List<AccountGrantDocument>();
        public long RevocationEpoch { get; init; }
    }

    internal sealed class SignedDeviceLeaseDocument
    {
        public DeviceLeaseDocument Lease { get; init; }
        public string SignerDeviceId { get; init; }
        public string SignatureBase64Url { get; init; }

        public bool Verify(string signerPublicKeyDerBase64Url) =>
            Lease != null && DeviceIdentityStore.Verify(
                signerPublicKeyDerBase64Url,
                DeviceTrustCodec.CanonicalLease(Lease),
                SignatureBase64Url);
    }

    internal static class DeviceTrustCodec
    {
        public static byte[] CanonicalLease(DeviceLeaseDocument lease)
        {
            if (lease == null) throw new ArgumentNullException(nameof(lease));
            StringBuilder builder = new StringBuilder("sdapp-device-lease/v1\n");
            Field(builder, "deviceId", lease.DeviceId);
            Field(builder, "role", lease.Role.ToString());
            Field(builder, "issuedAt", lease.IssuedAtEpochSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture));
            Field(builder, "expiresAt", lease.ExpiresAtEpochSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture));
            Field(builder, "revocationEpoch", lease.RevocationEpoch.ToString(System.Globalization.CultureInfo.InvariantCulture));
            List<AccountGrantDocument> grants = lease.AccountGrants.OrderBy(grant => grant.SteamId, StringComparer.Ordinal).ToList();
            Field(builder, "grantCount", grants.Count.ToString(System.Globalization.CultureInfo.InvariantCulture));
            for (int index = 0; index < grants.Count; index++)
            {
                AccountGrantDocument grant = grants[index];
                Field(builder, $"grant.{index}.steamId", grant.SteamId);
                Field(builder, $"grant.{index}.capabilities", string.Join(",", grant.Capabilities.Select(value => value.ToString()).OrderBy(value => value, StringComparer.Ordinal)));
                Field(builder, $"grant.{index}.issuedAt", grant.IssuedAtEpochSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture));
                Field(builder, $"grant.{index}.expiresAt", grant.ExpiresAtEpochSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture));
                Field(builder, $"grant.{index}.maximumUses", grant.MaximumUses?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty);
                Field(builder, $"grant.{index}.stepUp", grant.RequireStepUpAuthentication ? "1" : "0");
            }
            return Encoding.UTF8.GetBytes(builder.ToString());
        }

        private static void Field(StringBuilder builder, string name, string value)
        {
            value ??= string.Empty;
            builder.Append(name).Append('=').Append(Encoding.UTF8.GetByteCount(value)).Append(':').Append(value).Append('\n');
        }
    }

    internal sealed class SecurityAuditRecord
    {
        public string EventId { get; init; }
        public string DeviceId { get; init; }
        public string AccountSteamId { get; init; }
        public string Action { get; init; }
        public long OccurredAtEpochSeconds { get; init; }
        public bool Succeeded { get; init; }
        public string RelayObservedAddress { get; init; }
        public string PreviousHashBase64Url { get; init; }
    }

    internal static class SecurityAuditCodec
    {
        public static byte[] CanonicalRecord(SecurityAuditRecord record)
        {
            if (record == null) throw new ArgumentNullException(nameof(record));
            StringBuilder builder = new StringBuilder("sdapp-audit-event/v1\n");
            Field(builder, "eventId", record.EventId);
            Field(builder, "deviceId", record.DeviceId);
            Field(builder, "accountSteamId", record.AccountSteamId);
            Field(builder, "action", record.Action);
            Field(builder, "occurredAt", record.OccurredAtEpochSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture));
            Field(builder, "succeeded", record.Succeeded ? "1" : "0");
            Field(builder, "relayAddress", record.RelayObservedAddress);
            Field(builder, "previousHash", record.PreviousHashBase64Url);
            return Encoding.UTF8.GetBytes(builder.ToString());
        }

        public static string ComputeHash(SecurityAuditRecord record) =>
            Convert.ToBase64String(SHA256.HashData(CanonicalRecord(record))).TrimEnd('=').Replace('+', '-').Replace('/', '_');

        private static void Field(StringBuilder builder, string name, string value)
        {
            value ??= string.Empty;
            builder.Append(name).Append('=').Append(Encoding.UTF8.GetByteCount(value)).Append(':').Append(value).Append('\n');
        }
    }
}
