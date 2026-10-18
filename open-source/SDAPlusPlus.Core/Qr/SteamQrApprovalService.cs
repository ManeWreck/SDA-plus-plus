using System.Buffers.Binary;
using System.Collections.Specialized;
using System.Net;
using System.Security.Cryptography;
using Newtonsoft.Json.Linq;
using SDAPlusPlus.Core.Vault;
using SteamAuth;

namespace SDAPlusPlus.Core.Qr;

public sealed class SteamQrApprovalService : ISteamQrApprovalService
{
    public async Task<SteamQrApprovalResult> ApproveAsync(
        VaultAccount? account,
        string qrText,
        CancellationToken cancellationToken = default)
    {
        if (!SteamQrParser.TryParse(qrText, out SteamQrPayload? payload))
        {
            return new SteamQrApprovalResult(false, SteamQrApprovalFailure.InvalidQr);
        }

        if (account is null)
        {
            return new SteamQrApprovalResult(false, SteamQrApprovalFailure.MissingAccount);
        }

        SteamGuardAccount guard = account.GuardAccount;
        if (guard.Session is null)
        {
            return new SteamQrApprovalResult(false, SteamQrApprovalFailure.MissingSession);
        }

        if (guard.Session.IsRefreshTokenExpired())
        {
            return new SteamQrApprovalResult(false, SteamQrApprovalFailure.ExpiredSession);
        }

        if (string.IsNullOrWhiteSpace(guard.SharedSecret))
        {
            return new SteamQrApprovalResult(false, SteamQrApprovalFailure.MissingSecret);
        }

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (guard.Session.IsAccessTokenExpired())
            {
                await guard.Session.RefreshAccessToken();
            }

            cancellationToken.ThrowIfCancellationRequested();
            await GetAuthSessionInfoAsync(payload!.ClientId, guard.Session.AccessToken);

            byte[] signatureData = new byte[18];
            BinaryPrimitives.WriteUInt16LittleEndian(signatureData.AsSpan(0, 2), checked((ushort)payload.Version));
            BinaryPrimitives.WriteUInt64LittleEndian(signatureData.AsSpan(2, 8), payload.ClientId);
            BinaryPrimitives.WriteUInt64LittleEndian(signatureData.AsSpan(10, 8), guard.Session.SteamID);

            byte[] secret = Convert.FromBase64String(guard.SharedSecret);
            byte[] signature;
            try
            {
                using var hmac = new HMACSHA256(secret);
                signature = hmac.ComputeHash(signatureData);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(secret);
            }

            cancellationToken.ThrowIfCancellationRequested();
            await ConfirmAsync(guard, payload, signature);
            return new SteamQrApprovalResult(true);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (exception is FormatException or OverflowException or
                                           Newtonsoft.Json.JsonException or HttpRequestException or
                                           WebException or InvalidOperationException)
        {
            return new SteamQrApprovalResult(false, SteamQrApprovalFailure.SteamRejected);
        }
    }

    private static async Task GetAuthSessionInfoAsync(ulong clientId, string accessToken)
    {
        var body = new NameValueCollection { ["client_id"] = clientId.ToString() };
        string response = await SteamWeb.POSTRequest(
            "https://api.steampowered.com/IAuthenticationService/GetAuthSessionInfo/v1/?access_token=" + Uri.EscapeDataString(accessToken),
            null,
            body);
        JObject.Parse(response);
    }

    private static async Task ConfirmAsync(SteamGuardAccount account, SteamQrPayload payload, byte[] signature)
    {
        var body = new NameValueCollection
        {
            ["version"] = payload.Version.ToString(),
            ["client_id"] = payload.ClientId.ToString(),
            ["steamid"] = account.Session.SteamID.ToString(),
            ["signature"] = Convert.ToBase64String(signature),
            ["confirm"] = "true",
            ["persistence"] = "1"
        };
        string response = await SteamWeb.POSTRequest(
            "https://api.steampowered.com/IAuthenticationService/UpdateAuthSessionWithMobileConfirmation/v1/?access_token=" + Uri.EscapeDataString(account.Session.AccessToken),
            null,
            body);
        JObject.Parse(response);
    }
}
