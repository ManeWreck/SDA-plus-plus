using SteamAuth;

namespace SDAPlusPlus.Core.Enrollment;

public enum EnrollmentState
{
    PendingSave,
    RecoveryConfirmation,
    ReadyToFinalize,
    Finalizing,
    FinalSaveRequired,
    Completed,
    OutcomeUnknown
}

/// <summary>
/// Controls an already-issued authenticator. Persistence must durably save and
/// verify the account before returning; cancellation never deletes its secrets.
/// </summary>
public sealed class AuthenticatorEnrollment
{
    public const string PreparedCheckpoint = "prepared-v1";
    public const string SubmittedCheckpoint = "submitted-v1";
    public const string CompletedCheckpoint = "completed-v1";
    private readonly SteamGuardAccount account;
    private readonly Func<SteamGuardAccount, Task> persist;
    private readonly Func<string, Task<AuthenticatorLinker.FinalizeResult>> finalize;
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly string recoveryCode;

    public EnrollmentState State { get; private set; } = EnrollmentState.PendingSave;

    public AuthenticatorEnrollment(
        SteamGuardAccount account,
        Func<SteamGuardAccount, Task> persist,
        Func<string, Task<AuthenticatorLinker.FinalizeResult>> finalize,
        bool resume = false)
    {
        ArgumentNullException.ThrowIfNull(account);
        ArgumentNullException.ThrowIfNull(persist);
        ArgumentNullException.ThrowIfNull(finalize);
        if (string.IsNullOrWhiteSpace(account.SharedSecret) ||
            string.IsNullOrWhiteSpace(account.RevocationCode) || (!resume && account.FullyEnrolled))
            throw new ArgumentException("An unfinalized authenticator with recovery data is required.", nameof(account));

        this.account = account;
        this.persist = persist;
        this.finalize = finalize;
        recoveryCode = account.RevocationCode.Trim();
        if (resume)
        {
            State = account.EnrollmentCheckpoint == PreparedCheckpoint && !account.FullyEnrolled
                ? EnrollmentState.RecoveryConfirmation
                : account.EnrollmentCheckpoint == CompletedCheckpoint && account.FullyEnrolled
                    ? EnrollmentState.Completed
                    : EnrollmentState.OutcomeUnknown;
        }
    }

    public async Task SaveAsync()
    {
        if (!await gate.WaitAsync(0).ConfigureAwait(false))
            throw new InvalidOperationException("An enrollment operation is already in progress.");
        try
        {
            if (State is not (EnrollmentState.PendingSave or EnrollmentState.FinalSaveRequired))
                throw new InvalidOperationException("There is no pending save.");

            account.EnrollmentCheckpoint = State == EnrollmentState.PendingSave ? PreparedCheckpoint : CompletedCheckpoint;
            await persist(account).ConfigureAwait(false);
            State = State == EnrollmentState.PendingSave
                ? EnrollmentState.RecoveryConfirmation
                : EnrollmentState.Completed;
        }
        finally { gate.Release(); }
    }

    public bool ConfirmRecoveryCode(string? enteredCode)
    {
        if (!gate.Wait(0)) return false;
        try
        {
            if (State is not (EnrollmentState.RecoveryConfirmation or EnrollmentState.ReadyToFinalize))
                return false;

            bool matches = string.Equals(recoveryCode, enteredCode?.Trim(), StringComparison.OrdinalIgnoreCase);
            State = matches ? EnrollmentState.ReadyToFinalize : EnrollmentState.RecoveryConfirmation;
            return matches;
        }
        finally { gate.Release(); }
    }

    public async Task<AuthenticatorLinker.FinalizeResult> FinalizeAsync(string smsCode)
    {
        if (!await gate.WaitAsync(0).ConfigureAwait(false))
            throw new InvalidOperationException("An enrollment operation is already in progress.");
        try
        {
            if (State != EnrollmentState.ReadyToFinalize)
                throw new InvalidOperationException("Save the authenticator and confirm its recovery code first.");
            if (string.IsNullOrWhiteSpace(smsCode))
                throw new ArgumentException("The activation code is required.", nameof(smsCode));

            State = EnrollmentState.Finalizing;
            AuthenticatorLinker.FinalizeResult result;
            try
            {
                // Write-ahead checkpoint: after a crash a submitted request must not
                // become an apparently safe fresh activation attempt.
                account.EnrollmentCheckpoint = SubmittedCheckpoint;
                await persist(account).ConfigureAwait(false);
                result = await finalize(smsCode.Trim()).ConfigureAwait(false);
            }
            catch
            {
                // A lost response does not prove Steam rejected the enrollment.
                State = EnrollmentState.OutcomeUnknown;
                throw;
            }

            if (result == AuthenticatorLinker.FinalizeResult.Success)
            {
                account.FullyEnrolled = true;
                account.EnrollmentCheckpoint = CompletedCheckpoint;
                State = EnrollmentState.FinalSaveRequired;
                await persist(account).ConfigureAwait(false);
                State = EnrollmentState.Completed;
            }
            else
            {
                State = EnrollmentState.OutcomeUnknown;
                if (result == AuthenticatorLinker.FinalizeResult.BadSMSCode)
                {
                    account.EnrollmentCheckpoint = PreparedCheckpoint;
                    await persist(account).ConfigureAwait(false);
                    State = EnrollmentState.ReadyToFinalize;
                }
            }
            return result;
        }
        finally { gate.Release(); }
    }
}
