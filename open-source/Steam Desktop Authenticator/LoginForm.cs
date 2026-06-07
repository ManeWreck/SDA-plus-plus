using System;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;
using SteamAuth;
using SteamKit2;
using SteamKit2.Authentication;
using SteamKit2.Internal;

namespace Steam_Desktop_Authenticator
{
    public partial class LoginForm : Form
    {
        private readonly StoredCredentialLoginService storedCredentialLoginService = new StoredCredentialLoginService();
        public SteamGuardAccount account;
        public LoginType LoginReason;
        public SessionData Session;

        public LoginForm(LoginType loginReason = LoginType.Initial, SteamGuardAccount account = null)
        {
            InitializeComponent();
            Icon = Branding.LoadAppIcon();
            ModernUi.AttachWindowChrome(this, false, false);
            ModernUi.ShiftControlsDown(this, ModernUi.HeaderHeight + 8);
            ApplyLayout();
            ApplyTheme();
            ApplyLocalization();
            this.LoginReason = loginReason;
            this.account = account;

            try
            {
                if (this.LoginReason != LoginType.Initial)
                {
                    txtUsername.Text = account.AccountName;
                    txtUsername.Enabled = false;
                }

                if (this.LoginReason == LoginType.Refresh)
                {
                    labelLoginExplanation.Text = Localizer.Choose("Your Steam credentials have expired. For trade and market confirmations to work properly, please login again.", "Срок действия учетных данных Steam истек. Чтобы подтверждения обменов и маркета работали корректно, войдите заново.");
                }
                else if (this.LoginReason == LoginType.Import)
                {
                    labelLoginExplanation.Text = Localizer.Choose("Please login to your Steam account to import it.", "Войдите в аккаунт Steam, чтобы импортировать его.");
                }

                if (account?.Session != null)
                {
                    chkRememberPassword.Checked = storedCredentialLoginService.HasStoredCredentials(account.Session.SteamID, account.AccountName);
                }
            }
            catch (Exception)
            {
                MessageBox.Show("Не удалось найти ваш аккаунт. Попробуйте закрыть и снова открыть SDA.", "Ошибка входа", MessageBoxButtons.OK, MessageBoxIcon.Error);
                this.Close();
            }
        }

        private void ApplyLayout()
        {
            ClientSize = new Size(360, 250);
            MinimumSize = new Size(376, 250);

            label1.Location = new Point(18, 52);
            label1.Size = new Size(80, 24);

            txtUsername.Location = new Point(104, 50);
            txtUsername.Size = new Size(220, 25);

            label2.Location = new Point(18, 86);
            label2.Size = new Size(80, 24);

            txtPassword.Location = new Point(104, 84);
            txtPassword.Size = new Size(220, 25);

            labelLoginExplanation.AutoSize = false;
            labelLoginExplanation.Location = new Point(18, 122);
            labelLoginExplanation.Size = new Size(306, 44);

            chkRememberPassword.AutoSize = false;
            chkRememberPassword.Location = new Point(18, 202);
            chkRememberPassword.Size = new Size(188, 22);
            chkRememberPassword.TextAlign = ContentAlignment.MiddleLeft;

            btnSteamLogin.Location = new Point(214, 202);
            btnSteamLogin.Size = new Size(110, 34);
        }

        public void SetUsername(string username)
        {
            txtUsername.Text = username;
        }

        public string FilterPhoneNumber(string phoneNumber)
        {
            return phoneNumber.Replace("-", "").Replace("(", "").Replace(")", "");
        }

        public bool PhoneNumberOkay(string phoneNumber)
        {
            if (phoneNumber == null || phoneNumber.Length == 0) return false;
            if (phoneNumber[0] != '+') return false;
            return true;
        }

        private void ResetLoginButton()
        {
            btnSteamLogin.Enabled = true;
            btnSteamLogin.Text = "Войти";
        }

        private async void btnSteamLogin_Click(object sender, EventArgs e)
        {
            // Disable button while we login
            btnSteamLogin.Enabled = false;
            btnSteamLogin.Text = "Вход...";

            string username = txtUsername.Text;
            string password = txtPassword.Text;

            // Start a new SteamClient instance
            SteamClient steamClient = new SteamClient();

            // Connect to Steam
            steamClient.Connect();

            // Really basic way to wait until Steam is connected
            while (!steamClient.IsConnected)
                await Task.Delay(500);

            // Create a new auth session
            CredentialsAuthSession authSession;
            try
            {
                authSession = await steamClient.Authentication.BeginAuthSessionViaCredentialsAsync(new AuthSessionDetails
                {
                    Username = username,
                    Password = password,
                    IsPersistentSession = false,
                    PlatformType = EAuthTokenPlatformType.k_EAuthTokenPlatformType_MobileApp,
                    ClientOSType = EOSType.Android9,
                    Authenticator = new UserFormAuthenticator(this.account),
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Ошибка входа в Steam", MessageBoxButtons.OK, MessageBoxIcon.Error);
                this.Close();
                return;
            }

            // Starting polling Steam for authentication response
            AuthPollResult pollResponse;
            try
            {
                pollResponse = await authSession.PollingWaitForResultAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Ошибка входа в Steam", MessageBoxButtons.OK, MessageBoxIcon.Error);
                this.Close();
                return;
            }

            // Build a SessionData object
            SessionData sessionData = new SessionData()
            {
                SteamID = authSession.SteamID.ConvertToUInt64(),
                AccessToken = pollResponse.AccessToken,
                RefreshToken = pollResponse.RefreshToken,
            };

            //Login succeeded
            this.Session = sessionData;
            PersistStoredCredentials(sessionData.SteamID, account?.AccountName ?? username, username, password);

            // If we're only logging in for an account import, stop here
            if (LoginReason == LoginType.Import)
            {
                this.Close();
                return;
            }

            // If we're only logging in for a session refresh then save it and exit
            if (LoginReason == LoginType.Refresh)
            {
                Manifest man = Manifest.GetManifest();
                account.FullyEnrolled = true;
                account.Session = sessionData;
                HandleManifest(man, true);
                this.Close();
                return;
            }

            // Show a dialog to make sure they really want to add their authenticator
            var result = MessageBox.Show("Вход в аккаунт Steam выполнен успешно. Нажмите OK, чтобы продолжить привязку SDA как аутентификатора.", "Вход в Steam", MessageBoxButtons.OKCancel, MessageBoxIcon.Information);
            if (result == DialogResult.Cancel)
            {
                MessageBox.Show("Привязка аутентификатора отменена.", "Вход в Steam", MessageBoxButtons.OK, MessageBoxIcon.Error);
                ResetLoginButton();
                return;
            }

            // Begin linking mobile authenticator
            AuthenticatorLinker linker = new AuthenticatorLinker(sessionData);

            AuthenticatorLinker.LinkResult linkResponse = AuthenticatorLinker.LinkResult.GeneralFailure;
            while (linkResponse != AuthenticatorLinker.LinkResult.AwaitingFinalization)
            {
                try
                {
                    linkResponse = await linker.AddAuthenticator();
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Ошибка при добавлении аутентификатора: " + ex.Message, "Вход в Steam", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    ResetLoginButton();
                    return;
                }

                switch (linkResponse)
                {
                    case AuthenticatorLinker.LinkResult.MustProvidePhoneNumber:

                        // Show the phone input form
                        PhoneInputForm phoneInputForm = new PhoneInputForm(account);
                        phoneInputForm.ShowDialog();
                        if (phoneInputForm.Canceled)
                        {
                            this.Close();
                            return;
                        }

                        linker.PhoneNumber = phoneInputForm.PhoneNumber;
                        linker.PhoneCountryCode = phoneInputForm.CountryCode;
                        break;

                    case AuthenticatorLinker.LinkResult.AuthenticatorPresent:
                        MessageBox.Show("К этому аккаунту уже привязан аутентификатор. Чтобы добавить SDA как аутентификатор, сначала удалите текущий.", "Вход в Steam", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        this.Close();
                        return;

                    case AuthenticatorLinker.LinkResult.FailureAddingPhone:
                        MessageBox.Show("Не удалось добавить номер телефона. Попробуйте снова или используйте другой номер.", "Вход в Steam", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        linker.PhoneNumber = null;
                        break;

                    case AuthenticatorLinker.LinkResult.MustRemovePhoneNumber:
                        linker.PhoneNumber = null;
                        break;

                    case AuthenticatorLinker.LinkResult.MustConfirmEmail:
                        MessageBox.Show("Проверьте вашу почту и перейдите по ссылке от Steam перед продолжением.", "Вход в Steam", MessageBoxButtons.OK, MessageBoxIcon.Information);
                        break;

                    case AuthenticatorLinker.LinkResult.GeneralFailure:
                        MessageBox.Show("Ошибка при добавлении аутентификатора.", "Ошибка входа в Steam", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        this.Close();
                        return;
                }
            } // End while loop checking for AwaitingFinalization

            Manifest manifest = Manifest.GetManifest();
            string passKey = null;
            if (manifest.Entries.Count == 0)
            {
                passKey = manifest.PromptSetupPassKey("Введите ключ шифрования. Оставьте поле пустым или нажмите отмену, чтобы не шифровать (ОЧЕНЬ НЕБЕЗОПАСНО).");
            }
            else if (manifest.Entries.Count > 0 && manifest.Encrypted)
            {
                bool passKeyValid = false;
                while (!passKeyValid)
                {
                    InputForm passKeyForm = new InputForm("Введите текущий ключ шифрования.");
                    passKeyForm.ShowDialog();
                    if (!passKeyForm.Canceled)
                    {
                        passKey = passKeyForm.txtBox.Text;
                        passKeyValid = manifest.VerifyPasskey(passKey);
                        if (!passKeyValid)
                        {
                            MessageBox.Show("Неверный ключ шифрования. Введите тот же ключ, который использовался для других аккаунтов.");
                        }
                    }
                    else
                    {
                        this.Close();
                        return;
                    }
                }
            }

            //Save the file immediately; losing this would be bad.
            if (!manifest.SaveAccount(linker.LinkedAccount, passKey != null, passKey))
            {
                manifest.RemoveAccount(linker.LinkedAccount);
                MessageBox.Show("Не удалось сохранить файл мобильного аутентификатора. Аутентификатор не был привязан.");
                this.Close();
                return;
            }

            MessageBox.Show("Мобильный аутентификатор еще не привязан. Перед завершением обязательно запишите код отвязки: " + linker.LinkedAccount.RevocationCode);

            AuthenticatorLinker.FinalizeResult finalizeResponse = AuthenticatorLinker.FinalizeResult.GeneralFailure;
            while (finalizeResponse != AuthenticatorLinker.FinalizeResult.Success)
            {
                InputForm smsCodeForm = new InputForm("Введите SMS-код, отправленный на ваш телефон.");
                smsCodeForm.ShowDialog();
                if (smsCodeForm.Canceled)
                {
                    manifest.RemoveAccount(linker.LinkedAccount);
                    this.Close();
                    return;
                }

                InputForm confirmRevocationCode = new InputForm("Введите код отвязки, чтобы подтвердить, что вы его сохранили.");
                confirmRevocationCode.ShowDialog();
                if (confirmRevocationCode.txtBox.Text.ToUpper() != linker.LinkedAccount.RevocationCode)
                {
                    MessageBox.Show("Код отвязки введен неверно; аутентификатор не был привязан.");
                    manifest.RemoveAccount(linker.LinkedAccount);
                    this.Close();
                    return;
                }

                string smsCode = smsCodeForm.txtBox.Text;
                finalizeResponse = await linker.FinalizeAddAuthenticator(smsCode);

                switch (finalizeResponse)
                {
                    case AuthenticatorLinker.FinalizeResult.BadSMSCode:
                        continue;

                    case AuthenticatorLinker.FinalizeResult.UnableToGenerateCorrectCodes:
                        MessageBox.Show("Не удалось сгенерировать правильные коды для завершения привязки. Аутентификатор не должен был быть привязан. Если все же был привязан, срочно запишите код отвязки, это последний шанс его увидеть: " + linker.LinkedAccount.RevocationCode);
                        manifest.RemoveAccount(linker.LinkedAccount);
                        this.Close();
                        return;

                    case AuthenticatorLinker.FinalizeResult.GeneralFailure:
                        MessageBox.Show("Не удалось завершить привязку аутентификатора. Аутентификатор не должен был быть привязан. Если все же был привязан, срочно запишите код отвязки, это последний шанс его увидеть: " + linker.LinkedAccount.RevocationCode);
                        manifest.RemoveAccount(linker.LinkedAccount);
                        this.Close();
                        return;
                }
            }

            //Linked, finally. Re-save with FullyEnrolled property.
            manifest.SaveAccount(linker.LinkedAccount, passKey != null, passKey);
            MessageBox.Show("Мобильный аутентификатор успешно привязан. Обязательно запишите код отвязки: " + linker.LinkedAccount.RevocationCode);
            this.Close();
        }

        private void HandleManifest(Manifest man, bool IsRefreshing = false)
        {
            string passKey = null;
            if (man.Entries.Count == 0)
            {
                passKey = man.PromptSetupPassKey("Введите ключ шифрования. Оставьте поле пустым или нажмите отмену, чтобы не шифровать (ОЧЕНЬ НЕБЕЗОПАСНО).");
            }
            else if (man.Entries.Count > 0 && man.Encrypted)
            {
                bool passKeyValid = false;
                while (!passKeyValid)
                {
                    InputForm passKeyForm = new InputForm("Введите текущий ключ шифрования.");
                    passKeyForm.ShowDialog();
                    if (!passKeyForm.Canceled)
                    {
                        passKey = passKeyForm.txtBox.Text;
                        passKeyValid = man.VerifyPasskey(passKey);
                        if (!passKeyValid)
                        {
                            MessageBox.Show("Неверный ключ шифрования. Введите тот же ключ, который использовался для других аккаунтов.", "Вход в Steam", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        }
                    }
                    else
                    {
                        this.Close();
                        return;
                    }
                }
            }

            man.SaveAccount(account, passKey != null, passKey);
            if (IsRefreshing)
            {
                MessageBox.Show("Сессия была обновлена.", "Вход в Steam", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            else
            {
                MessageBox.Show("Мобильный аутентификатор успешно привязан. Обязательно запишите код отвязки: " + account.RevocationCode, "Вход в Steam", MessageBoxButtons.OK);
            }
            this.Close();
        }

        private void LoginForm_Load(object sender, EventArgs e)
        {
            if (account != null && account.AccountName != null)
            {
                txtUsername.Text = account.AccountName;
            }
        }

        private void ApplyTheme()
        {
            BackColor = Branding.WindowBackground;
            ForeColor = Branding.HeadingText;
            label1.ForeColor = Branding.MutedText;
            label2.ForeColor = Branding.MutedText;
            labelLoginExplanation.ForeColor = Branding.MutedText;
            labelLoginExplanation.BackColor = Branding.WindowBackground;
            chkRememberPassword.ForeColor = Branding.HeadingText;
            chkRememberPassword.BackColor = Branding.WindowBackground;
            ModernUi.WrapTextBox(txtUsername);
            ModernUi.WrapTextBox(txtPassword);
            ModernUi.RoundButton(btnSteamLogin, true);
            Paint += ModernUi.PaintGlassBackground;
        }

        private void ApplyLocalization()
        {
            Text = Localizer.Choose("Steam Login", "Вход в Steam");
            label1.Text = Localizer.Choose("Username:", "Логин:");
            label2.Text = Localizer.Choose("Password:", "Пароль:");
            btnSteamLogin.Text = Localizer.Choose("Login", "Войти");
            labelLoginExplanation.Text = Localizer.Choose(
                "This will activate Steam Desktop Authenticator on your Steam account. This requires a phone number that can receive SMS.",
                "Это активирует Steam Desktop Authenticator для вашего аккаунта Steam. Для этого нужен номер телефона, который может получать SMS.");
            chkRememberPassword.Text = Localizer.Choose("Store password encrypted for auto-login", "Сохранить пароль в зашифрованном виде для автовхода");
        }

        private void PersistStoredCredentials(ulong steamId, string accountName, string username, string password)
        {
            if (chkRememberPassword.Checked)
            {
                storedCredentialLoginService.SaveCredentials(steamId, accountName, username, password);
                return;
            }

            storedCredentialLoginService.RemoveCredentials(steamId, accountName);
        }

        public enum LoginType
        {
            Initial,
            Refresh,
            Import
        }
    }
}
