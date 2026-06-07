using SteamAuth;
using SteamKit2;
using SteamKit2.Authentication;
using SteamKit2.Internal;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Steam_Desktop_Authenticator
{
    internal sealed class StoredCredentialLoginService
    {
        private readonly StoredCredentialsVault vault = new StoredCredentialsVault();

        public sealed class RestoreResult
        {
            public bool Success { get; set; }
            public bool CredentialsAvailable { get; set; }
            public string Message { get; set; }
        }

        public bool HasStoredCredentials(ulong steamId, string accountName = null)
        {
            return vault.HasCredentials(steamId, accountName);
        }

        public List<StoredCredentialsVault.StoredCredential> GetAllCredentials() => vault.GetAllCredentials();

        public StoredCredentialsVault.VaultStatus GetVaultStatus() => vault.GetStatus();

        public async Task<RestoreResult> TryRestoreSessionAsync(SteamGuardAccount account)
        {
            if (account?.Session == null)
            {
                return new RestoreResult
                {
                    Success = false,
                    CredentialsAvailable = false,
                    Message = "No account session is loaded."
                };
            }

            if (!vault.TryGetCredentials(account.Session.SteamID, account.AccountName, out StoredCredentialsVault.StoredCredential credential))
            {
                return new RestoreResult
                {
                    Success = false,
                    CredentialsAvailable = false,
                    Message = "No stored credentials were found for this account."
                };
            }

            SteamClient steamClient = new SteamClient();

            try
            {
                steamClient.Connect();
                while (!steamClient.IsConnected)
                {
                    await Task.Delay(250);
                }

                CredentialsAuthSession authSession = await steamClient.Authentication.BeginAuthSessionViaCredentialsAsync(new AuthSessionDetails
                {
                    Username = credential.Username,
                    Password = credential.Password,
                    IsPersistentSession = false,
                    PlatformType = EAuthTokenPlatformType.k_EAuthTokenPlatformType_MobileApp,
                    ClientOSType = EOSType.Android9,
                    Authenticator = new SilentAccountAuthenticator(account),
                });

                AuthPollResult pollResponse = await authSession.PollingWaitForResultAsync();
                account.Session = new SessionData
                {
                    SteamID = authSession.SteamID.ConvertToUInt64(),
                    AccessToken = pollResponse.AccessToken,
                    RefreshToken = pollResponse.RefreshToken,
                };

                return new RestoreResult
                {
                    Success = true,
                    CredentialsAvailable = true,
                    Message = "SDA++ restored the session automatically."
                };
            }
            catch (Exception ex)
            {
                return new RestoreResult
                {
                    Success = false,
                    CredentialsAvailable = true,
                    Message = ex.Message
                };
            }
            finally
            {
                try
                {
                    steamClient.Disconnect();
                }
                catch
                {
                }
            }
        }

        public void SaveCredentials(ulong steamId, string accountName, string username, string password)
        {
            vault.SaveCredentials(steamId, accountName, username, password);
        }

        public void RemoveCredentials(ulong steamId, string accountName = null)
        {
            vault.RemoveCredentials(steamId, accountName);
        }

        public StoredCredentialsVault GetVault() => vault;
    }
}
