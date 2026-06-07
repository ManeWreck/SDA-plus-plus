using SteamAuth;
using SteamKit2.Authentication;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Steam_Desktop_Authenticator
{
    internal class UserFormAuthenticator : IAuthenticator
    {
        private SteamGuardAccount account;
        private int deviceCodesGenerated = 0;

        public UserFormAuthenticator(SteamGuardAccount account)
        {
            this.account = account;
        }

        public Task<bool> AcceptDeviceConfirmationAsync()
        {
            return Task.FromResult(false);
        }

        public async Task<string> GetDeviceCodeAsync(bool previousCodeWasIncorrect)
        {
            // If a code fails wait 30 seconds for a new one to regenerate
            if (previousCodeWasIncorrect)
            {
                // After 2 tries tell the user that there seems to be an issue
                if (deviceCodesGenerated > 2)
                    MessageBox.Show("Похоже, возникла проблема со входом по этим двухфакторным кодам. Убедитесь, что SDA все еще является вашим аутентификатором.");

                await Task.Delay(30000);
            }

            string deviceCode;

            if (account == null)
            {
                MessageBox.Show("К этому аккаунту уже привязан аутентификатор. Чтобы добавить SDA как аутентификатор, сначала удалите текущий.", "Вход в Steam", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return null;
            }
            else
            {
                deviceCode = await account.GenerateSteamGuardCodeAsync();
                deviceCodesGenerated++;
            }

            return deviceCode;
        }

        public Task<string> GetEmailCodeAsync(string email, bool previousCodeWasIncorrect)
        {
            string message = "Введите код, отправленный на вашу почту:";
            if (previousCodeWasIncorrect)
            {
                message = "Введенный код неверный. Введите код, отправленный на вашу почту:";
            }

            InputForm emailForm = new InputForm(message);
            emailForm.ShowDialog();
            return Task.FromResult(emailForm.txtBox.Text);
        }
    }
}
