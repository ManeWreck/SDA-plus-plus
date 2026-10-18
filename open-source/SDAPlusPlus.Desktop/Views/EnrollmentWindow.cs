using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using SDAPlusPlus.Core.Enrollment;
using SDAPlusPlus.Core.Sessions;
using SDAPlusPlus.Core.Vault;
using SteamAuth;

namespace SDAPlusPlus.Desktop.Views;

public sealed class EnrollmentWindow : Window
{
    private readonly string directory;
    private readonly bool russian;
    private readonly StackPanel panel = new() { Spacing = 12, Margin = new Thickness(24) };
    private readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap };
    private readonly TextBox login = new();
    private readonly TextBox password = new() { PasswordChar = '*' };
    private readonly TextBox vaultPassword = new() { PasswordChar = '*' };
    private AuthenticatorEnrollment? enrollment;
    private SteamGuardAccount? issued;
    private bool busy;
    private bool authenticatorRequested;
    private string encryptionPassword = string.Empty;

    public EnrollmentWindow(string directory, bool russian)
    {
        this.directory = directory;
        this.russian = russian;
        Title = T("Создать .maFile", "Create .maFile");
        Width = 620;
        Height = 700;
        MinWidth = 460;
        MinHeight = 480;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Content = new ScrollViewer { Content = panel };
        Closing += (_, e) =>
        {
            if (busy || enrollment?.State is EnrollmentState.PendingSave or EnrollmentState.FinalSaveRequired)
            {
                e.Cancel = true;
                status.Text = T("Сначала дождитесь операции и сохраните полученный .maFile. Закрытие сейчас может привести к потере данных.",
                    "Wait for the operation and save the issued .maFile before closing to avoid losing its secrets.");
            }
        };
        Closed += (_, _) => { password.Text = null; vaultPassword.Text = null; encryptionPassword = string.Empty; issued = null; enrollment = null; };
        Heading(T("1. Подготовка аккаунта", "1. Prepare the account"));
        Explain(T("Это подключение нового Steam Guard, а не импорт. Нужен подтверждённый номер телефона в Steam. Существующий аутентификатор мастер не удаляет и не заменяет.",
            "This enrolls a new Steam Guard, not an import. Verify a phone number in Steam first. This wizard never removes or replaces an existing authenticator."));
        Explain(T("Перед началом зашифруйте хранилище на вкладке «Шифрование». Для нового пустого хранилища задайте пароль от 12 символов. Сохраните его отдельно: восстановить его приложение не сможет.",
            "Encrypt your existing vault in Encryption before starting. For a new empty vault, choose a password of at least 12 characters and keep it separately; the app cannot recover it."));
        Field(T("Логин Steam", "Steam login"), login);
        Field(T("Пароль Steam", "Steam password"), password);
        Field(T("Пароль шифрования .maFile (не пароль Steam)", ".maFile encryption password (not the Steam password)"), vaultPassword);
        Explain(directory);
        Button(T("Войти и запросить новый аутентификатор", "Sign in and request a new authenticator"), BeginAsync);
        Button(T("Продолжить сохранённую привязку", "Resume a saved enrollment"), ResumeAsync);
        panel.Children.Add(status);
    }

    private Task ResumeAsync()
    {
        encryptionPassword = vaultPassword.Text ?? string.Empty;
        var loaded = new LegacyVaultReader().Load(directory, encryptionPassword);
        if (loaded.State != VaultLoadState.Ready)
            throw new InvalidOperationException(T("Введите пароль хранилища. Все его файлы должны успешно читаться.", "Enter the vault password. All vault files must be readable."));
        var candidates = loaded.Accounts.Where(a => !a.GuardAccount.FullyEnrolled &&
            !string.IsNullOrWhiteSpace(a.GuardAccount.RevocationCode)).ToArray();
        if (candidates.Length == 0)
            throw new InvalidOperationException(T("Сохранённых незавершённых привязок нет.", "No saved pending enrollments found."));
        panel.Children.Clear();
        Heading(T("Продолжение привязки", "Resume enrollment"));
        Explain(T("Выберите аккаунт. Новый аутентификатор запрашиваться не будет. Recovery-код потребуется подтвердить снова.",
            "Choose an account. No new authenticator will be requested. Confirm the recovery code again to continue."));
        var selection = new ComboBox
        {
            ItemsSource = candidates.Select(a => a.Name + " / " + a.SteamId).ToArray(),
            SelectedIndex = 0,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        panel.Children.Add(selection);
        Button(T("Открыть сохранённый этап", "Open saved stage"), () =>
        {
            issued = candidates[selection.SelectedIndex].GuardAccount;
            Func<string, Task<AuthenticatorLinker.FinalizeResult>> finalizer =
                _ => throw new InvalidOperationException("Unknown enrollment cannot be submitted.");
            if (issued.EnrollmentCheckpoint == AuthenticatorEnrollment.PreparedCheckpoint)
                finalizer = AuthenticatorLinker.Resume(issued).FinalizeAddAuthenticator;
            enrollment = new AuthenticatorEnrollment(issued, PersistAsync, finalizer, resume: true);
            authenticatorRequested = true;
            ShowRecovery();
            return Task.CompletedTask;
        });
        panel.Children.Add(status);
        return Task.CompletedTask;
    }

    private async Task BeginAsync()
    {
        if (authenticatorRequested)
            throw new InvalidOperationException(T("Запрос уже отправлялся. Проверьте состояние аккаунта в Steam, прежде чем начинать новый мастер.", "A request was already sent. Check the account state in Steam before starting a new wizard."));
        if (string.IsNullOrWhiteSpace(login.Text) || string.IsNullOrEmpty(password.Text) || string.IsNullOrEmpty(vaultPassword.Text))
            throw new InvalidOperationException(T("Заполните все поля.", "Complete every field."));
        var reader = new LegacyVaultReader();
        bool hasManifest = File.Exists(Path.Combine(directory, "manifest.json"));
        if (hasManifest && reader.Load(directory).State != VaultLoadState.Locked)
            throw new InvalidOperationException(T("Сначала зашифруйте существующее хранилище на вкладке «Шифрование».", "Encrypt the existing vault in Encryption first."));
        if (hasManifest && reader.Load(directory, vaultPassword.Text).State != VaultLoadState.Ready)
            throw new InvalidOperationException(T("Не удалось полностью открыть хранилище. Проверьте пароль и файлы.", "The vault could not be fully opened. Check its password and files."));
        if (!hasManifest && (vaultPassword.Text.Length < 12 || reader.Load(directory).Accounts.Count > 0))
            throw new InvalidOperationException(T("Нужен пароль от 12 символов и пустое хранилище, либо восстановленный manifest существующего хранилища.", "Use a password of at least 12 characters and an empty vault, or restore the existing vault manifest."));
        string name = login.Text.Trim();
        encryptionPassword = vaultPassword.Text;
        var authenticated = await new SteamSessionLoginService().LoginAsync(new SteamGuardAccount(), name, password.Text,
            (_, _, _) => Dispatcher.UIThread.InvokeAsync(RequestEmailCodeAsync));
        password.Text = null;
        if (authenticated.Session is null)
            throw new InvalidOperationException(T("Вход не завершён. Проверьте данные и подтверждение почты. Существующий Steam Guard не заменяется.",
                "Sign-in was not completed. Check credentials and email confirmation. Existing Steam Guard is not replaced."));
        if (reader.Load(directory, vaultPassword.Text).Accounts.Any(a => a.SteamId == authenticated.Session.SteamID.ToString()))
            throw new InvalidOperationException(T("Этот аккаунт уже есть в хранилище.", "This account is already in the vault."));

        var linker = new AuthenticatorLinker(authenticated.Session);
        authenticatorRequested = true;
        var result = await linker.AddAuthenticator();
        if (result != AuthenticatorLinker.LinkResult.AwaitingFinalization)
            throw new InvalidOperationException(result switch
            {
                AuthenticatorLinker.LinkResult.AuthenticatorPresent => T("Steam сообщает, что аутентификатор уже подключён. Используйте импорт существующего .maFile.", "Steam reports an existing authenticator. Import its .maFile instead."),
                AuthenticatorLinker.LinkResult.MustProvidePhoneNumber => T("Сначала добавьте и подтвердите номер телефона в Steam.", "Add and verify a phone number in Steam first."),
                _ => T("Steam не выдал новый аутентификатор. Не повторяйте запрос без проверки состояния аккаунта.", "Steam did not issue an authenticator. Check the account state before retrying.")
            });
        issued = linker.LinkedAccount;
        issued.AccountName = name;
        enrollment = new AuthenticatorEnrollment(issued, PersistAsync, linker.FinalizeAddAuthenticator);
        ShowRecovery();
        await enrollment.SaveAsync();
        status.Text = T(".maFile сохранён в зашифрованном хранилище. Привязка ещё не завершена.", ".maFile saved in the encrypted vault. Enrollment is not finished yet.");
    }

    private Task PersistAsync(SteamGuardAccount account)
    {
        var reader = new LegacyVaultReader();
        var record = new VaultAccount(account.AccountName, account.Session.SteamID.ToString(), account, true, false);
        var existing = reader.Load(directory, encryptionPassword).Accounts.FirstOrDefault(a => a.SteamId == record.SteamId);
        if (existing is null)
        {
            var result = new LegacyVaultAccountImportService(reader).ImportAccount(record, directory, encryptionPassword);
            if (!result.IsSuccess) throw new IOException(T("Не удалось сохранить .maFile. Освободите место и повторите сохранение.", "Could not save .maFile. Free disk space and retry saving."));
        }
        else
        {
            if (existing.GuardAccount.SharedSecret != account.SharedSecret)
                throw new IOException(T("В хранилище другой аутентификатор этого аккаунта. Перезапись запрещена.", "The vault contains a different authenticator for this account. Overwrite refused."));
            if (!new LegacyVaultAccountWriter(reader).SaveSession(directory, record, encryptionPassword).IsSuccess)
                throw new IOException(T("Не удалось сохранить состояние. Повторите сохранение, не привязку.", "Could not save the state. Retry saving, not enrollment."));
        }
        var saved = reader.Load(directory, encryptionPassword).Accounts.FirstOrDefault(a => a.SteamId == record.SteamId);
        if (saved?.GuardAccount.SharedSecret != account.SharedSecret || saved.GuardAccount.RevocationCode != account.RevocationCode || saved.GuardAccount.FullyEnrolled != account.FullyEnrolled || saved.GuardAccount.EnrollmentCheckpoint != account.EnrollmentCheckpoint)
            throw new IOException(T("Проверка сохранённого файла не прошла.", "Saved file verification failed."));
        return Task.CompletedTask;
    }

    private void ShowRecovery()
    {
        panel.Children.Clear();
        Heading(T("2. Сохраните recovery-код", "2. Save the recovery code"));
        Explain(T("Запишите этот код отдельно от компьютера. Он нужен для отключения аутентификатора при потере доступа. Не публикуйте его и не отправляйте в поддержку.",
            "Keep this code separately from this computer. It removes the authenticator if you lose access. Do not publish it or send it to support."));
        panel.Children.Add(new SelectableTextBlock { Text = issued!.RevocationCode, FontSize = 28, FontWeight = FontWeight.Bold });
        Explain(T("При закрытии уже сохранённый .maFile не удаляется. После неопределённого ответа Steam проверьте состояние аккаунта, не начинайте новую привязку.",
            "Closing never deletes a saved .maFile. After an uncertain Steam response, check the account instead of starting another enrollment."));
        if (enrollment!.State == EnrollmentState.OutcomeUnknown)
        {
            Explain(T("Результат предыдущей попытки неизвестен либо файл создан старой версией без отметки этапа. Повторная активация заблокирована. Проверьте Steam Guard в официальном клиенте; не удаляйте этот файл. Автоматическая сверка со Steam пока не реализована.",
                "The previous outcome is unknown, or this legacy file has no checkpoint. Another activation is blocked. Check Steam Guard in the official client and keep this file. Automatic Steam reconciliation is not implemented yet."));
            panel.Children.Add(status);
            return;
        }
        var recovery = new TextBox { PasswordChar = '*' };
        var sms = new TextBox();
        Field(T("Введите сохранённый recovery-код повторно", "Re-enter your saved recovery code"), recovery);
        Field(T("Код активации из SMS", "SMS activation code"), sms);
        Button(T("Подтвердить код и завершить привязку", "Confirm recovery code and finish enrollment"), async () =>
        {
            if (!enrollment!.ConfirmRecoveryCode(recovery.Text))
                throw new InvalidOperationException(T("Recovery-код не совпадает либо сначала требуется сохранение файла.", "Recovery code does not match, or the file must be saved first."));
            var result = await enrollment.FinalizeAsync(sms.Text ?? "");
            recovery.Text = null;
            sms.Text = null;
            status.Text = result == AuthenticatorLinker.FinalizeResult.Success
                ? T("Steam Guard подключён, .maFile сохранён и проверен. Можно закрыть мастер.", "Steam Guard enrolled; .maFile saved and verified. You can close the wizard.")
                : result == AuthenticatorLinker.FinalizeResult.BadSMSCode
                    ? T("Неверный SMS-код. Введите его повторно.", "Incorrect SMS code. Try again.")
                    : T("Состояние привязки неизвестно. Данные сохранены; проверьте аккаунт в Steam.", "Enrollment status is unknown. Secrets are saved; check the account in Steam.");
        });
        Button(T("Повторить только сохранение .maFile", "Retry saving .maFile only"), async () =>
        {
            await enrollment!.SaveAsync();
            status.Text = enrollment.State == EnrollmentState.Completed
                ? T("Привязка завершена, файл сохранён.", "Enrollment complete; file saved.")
                : T("Файл сохранён. Теперь подтвердите recovery-код и SMS.", "File saved. Confirm the recovery and SMS codes next.");
        });
        panel.Children.Add(status);
    }

    private async Task<string?> RequestEmailCodeAsync()
    {
        var entry = new TextBox();
        var submit = new Button { Content = T("Подтвердить", "Confirm") };
        var dialog = new Window { Title = T("Код из письма Steam", "Steam email code"), Width = 420, Height = 180, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        dialog.Content = new StackPanel { Margin = new Thickness(20), Spacing = 12, Children = { entry, submit } };
        submit.Click += (_, _) => dialog.Close(entry.Text);
        return await dialog.ShowDialog<string?>(this);
    }

    private void Button(string title, Func<Task> action)
    {
        var button = new Button { Content = title, HorizontalAlignment = HorizontalAlignment.Stretch };
        button.Click += async (_, _) =>
        {
            if (busy) return;
            busy = true;
            panel.IsEnabled = false;
            button.IsEnabled = false;
            status.Text = T("Выполняется...", "Working...");
            try { await action(); }
            catch (Exception error)
            {
                // Never display raw network exceptions: they can contain token-bearing URLs.
                status.Text = error is IOException or InvalidOperationException or ArgumentException
                    ? error.Message : T("Операция не завершена. Проверьте соединение и состояние аккаунта. Сохранённые файлы не удалены.", "Operation not completed. Check the connection and account state. Saved files were not deleted.");
            }
            finally { busy = false; button.IsEnabled = true; panel.IsEnabled = true; }
        };
        panel.Children.Add(button);
    }

    private string T(string ru, string en) => russian ? ru : en;
    private void Heading(string text) => panel.Children.Add(new TextBlock { Text = text, FontSize = 24, FontWeight = FontWeight.Bold });
    private void Explain(string text) => panel.Children.Add(new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap });
    private void Field(string text, TextBox field) { Explain(text); panel.Children.Add(field); }
}
