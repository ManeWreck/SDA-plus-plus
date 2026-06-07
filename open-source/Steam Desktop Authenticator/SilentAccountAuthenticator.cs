using SteamAuth;
using SteamKit2.Authentication;
using System.Threading.Tasks;

namespace Steam_Desktop_Authenticator
{
    internal sealed class SilentAccountAuthenticator : IAuthenticator
    {
        private readonly SteamGuardAccount account;

        public SilentAccountAuthenticator(SteamGuardAccount account)
        {
            this.account = account;
        }

        public Task<bool> AcceptDeviceConfirmationAsync()
        {
            return Task.FromResult(false);
        }

        public Task<string> GetEmailCodeAsync(string email, bool previousCodeWasIncorrect)
        {
            return Task.FromResult<string>(null);
        }

        public async Task<string> GetDeviceCodeAsync(bool previousCodeWasIncorrect)
        {
            if (account == null)
            {
                return null;
            }

            if (previousCodeWasIncorrect)
            {
                await Task.Delay(30000);
            }

            return await account.GenerateSteamGuardCodeAsync();
        }
    }
}
