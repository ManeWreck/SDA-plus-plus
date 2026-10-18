using SteamAuth;
using SteamKit2;
using SteamKit2.Authentication;
using SteamKit2.Internal;

namespace SDAPlusPlus.Core.Sessions;

public sealed class SteamSessionLoginService : ISteamSessionLoginService
{
    private static readonly TimeSpan ConnectionTimeout = TimeSpan.FromSeconds(20);

    public async Task<SteamLoginResult> LoginAsync(
        SteamGuardAccount account,
        string username,
        string password,
        Func<string, bool, CancellationToken, Task<string?>>? requestEmailCode = null,
        CancellationToken cancellationToken = default)
    {
        if (account is null || string.IsNullOrWhiteSpace(username) || string.IsNullOrEmpty(password))
        {
            return new SteamLoginResult(SteamLoginState.InvalidInput, Detail: "Username and password are required.");
        }

        var steamClient = new SteamClient();
        try
        {
            steamClient.Connect();
            using var connectionTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            connectionTimeout.CancelAfter(ConnectionTimeout);
            while (!steamClient.IsConnected)
            {
                await Task.Delay(100, connectionTimeout.Token).ConfigureAwait(false);
            }

            var authenticator = new AccountAuthenticator(account, requestEmailCode, cancellationToken);
            CredentialsAuthSession authSession = await steamClient.Authentication.BeginAuthSessionViaCredentialsAsync(
                new AuthSessionDetails
                {
                    Username = username.Trim(),
                    Password = password,
                    IsPersistentSession = true,
                    PlatformType = EAuthTokenPlatformType.k_EAuthTokenPlatformType_MobileApp,
                    ClientOSType = EOSType.Android9,
                    Authenticator = authenticator
                }).WaitAsync(cancellationToken).ConfigureAwait(false);

            AuthPollResult pollResult = await authSession.PollingWaitForResultAsync()
                .WaitAsync(cancellationToken)
                .ConfigureAwait(false);

            return new SteamLoginResult(
                SteamLoginState.Succeeded,
                new SessionData
                {
                    SteamID = authSession.SteamID.ConvertToUInt64(),
                    AccessToken = pollResult.AccessToken,
                    RefreshToken = pollResult.RefreshToken
                });
        }
        catch (EmailCodeUnavailableException)
        {
            return new SteamLoginResult(SteamLoginState.EmailCodeRequired, Detail: "Steam requested an email verification code.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new SteamLoginResult(SteamLoginState.Cancelled);
        }
        catch (OperationCanceledException)
        {
            return new SteamLoginResult(SteamLoginState.ConnectionFailed, Detail: "Steam did not respond before the connection timed out.");
        }
        catch (Exception exception)
        {
            return new SteamLoginResult(SteamLoginState.AuthenticationFailed, Detail: SanitizeError(exception.Message));
        }
        finally
        {
            steamClient.Disconnect();
        }
    }

    private static string SanitizeError(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return "Steam authentication failed.";
        }

        return message.Length <= 300 ? message : message[..300];
    }

    private sealed class AccountAuthenticator(
        SteamGuardAccount account,
        Func<string, bool, CancellationToken, Task<string?>>? requestEmailCode,
        CancellationToken cancellationToken) : IAuthenticator
    {
        public Task<bool> AcceptDeviceConfirmationAsync() => Task.FromResult(false);

        public async Task<string> GetEmailCodeAsync(string email, bool previousCodeWasIncorrect)
        {
            if (requestEmailCode is null)
            {
                throw new EmailCodeUnavailableException();
            }

            string? code = await requestEmailCode(email, previousCodeWasIncorrect, cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(code))
            {
                throw new EmailCodeUnavailableException();
            }

            return code.Trim();
        }

        public async Task<string> GetDeviceCodeAsync(bool previousCodeWasIncorrect)
        {
            if (previousCodeWasIncorrect)
            {
                await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken).ConfigureAwait(false);
            }

            return await account.GenerateSteamGuardCodeAsync().ConfigureAwait(false);
        }
    }

    private sealed class EmailCodeUnavailableException : Exception;
}
