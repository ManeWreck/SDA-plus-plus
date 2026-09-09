using SDAPlusPlus.Core.Enrollment;
using SteamAuth;

namespace SDAPlusPlus.Core.Tests;

public sealed class AuthenticatorEnrollmentTests
{
    [Fact]
    public async Task ResumePreparedRequiresRecoveryConfirmationAgain()
    {
        string saved = "";
        var flow = new AuthenticatorEnrollment(Account(), a =>
        {
            saved = Newtonsoft.Json.JsonConvert.SerializeObject(a);
            return Task.CompletedTask;
        }, _ => throw new Exception("No request expected"));
        await flow.SaveAsync();
        flow.ConfirmRecoveryCode("R12345");
        var restored = Newtonsoft.Json.JsonConvert.DeserializeObject<SteamGuardAccount>(saved)!;
        var resumed = new AuthenticatorEnrollment(restored, _ => Task.CompletedTask,
            _ => Task.FromResult(AuthenticatorLinker.FinalizeResult.Success), resume: true);
        Assert.Equal(EnrollmentState.RecoveryConfirmation, resumed.State);
        await Assert.ThrowsAsync<InvalidOperationException>(() => resumed.FinalizeAsync("12345"));
        Assert.True(resumed.ConfirmRecoveryCode("R12345"));
        await resumed.FinalizeAsync("12345");
        Assert.Equal(EnrollmentState.Completed, resumed.State);
    }

    [Fact]
    public async Task SubmittedCheckpointIsSavedBeforeNetworkAndBlocksReplayAfterRestart()
    {
        string saved = "";
        var flow = new AuthenticatorEnrollment(Account(), a =>
        {
            saved = Newtonsoft.Json.JsonConvert.SerializeObject(a);
            return Task.CompletedTask;
        }, _ =>
        {
            Assert.Contains(AuthenticatorEnrollment.SubmittedCheckpoint, saved);
            throw new HttpRequestException();
        });
        await flow.SaveAsync();
        flow.ConfirmRecoveryCode("R12345");
        await Assert.ThrowsAsync<HttpRequestException>(() => flow.FinalizeAsync("12345"));
        var restored = Newtonsoft.Json.JsonConvert.DeserializeObject<SteamGuardAccount>(saved)!;
        var resumed = new AuthenticatorEnrollment(restored, _ => Task.CompletedTask,
            _ => throw new Exception("Must not replay"), resume: true);
        Assert.Equal(EnrollmentState.OutcomeUnknown, resumed.State);
        Assert.False(resumed.ConfirmRecoveryCode("R12345"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => resumed.FinalizeAsync("12345"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("unknown-future-version")]
    public void LegacyOrUnknownCheckpointCannotBeAutomaticallyResumed(string? checkpoint)
    {
        var account = Account();
        account.EnrollmentCheckpoint = checkpoint;
        var flow = new AuthenticatorEnrollment(account, _ => Task.CompletedTask,
            _ => throw new Exception("Must not submit"), resume: true);
        Assert.Equal(EnrollmentState.OutcomeUnknown, flow.State);
    }

    [Fact]
    public async Task CheckpointWriteFailureNeverSendsActivation()
    {
        int saves = 0;
        var flow = new AuthenticatorEnrollment(Account(), _ =>
        {
            if (++saves == 2) throw new IOException();
            return Task.CompletedTask;
        }, _ => throw new Exception("Must not submit"));
        await flow.SaveAsync();
        flow.ConfirmRecoveryCode("R12345");
        await Assert.ThrowsAsync<IOException>(() => flow.FinalizeAsync("12345"));
        Assert.Equal(EnrollmentState.OutcomeUnknown, flow.State);
    }

    [Fact]
    public void LinkerResumePreservesOriginalDeviceAndSecrets()
    {
        var account = Account();
        account.DeviceID = "android:test-device";
        account.Session = new SessionData { SteamID = 76561198000000000, AccessToken = "test-only" };
        var linker = AuthenticatorLinker.Resume(account);
        Assert.Same(account, linker.LinkedAccount);
        Assert.Equal(account.DeviceID, linker.DeviceID);
    }

    private static SteamGuardAccount Account() => new()
    {
        SharedSecret = "MDEyMzQ1Njc4OWFiY2RlZg==",
        RevocationCode = "R12345",
        AccountName = "test-only"
    };

    [Fact]
    public async Task CannotFinalizeBeforeSaveAndRecoveryConfirmation()
    {
        int requests = 0;
        var flow = new AuthenticatorEnrollment(Account(), _ => Task.CompletedTask, _ =>
        {
            requests++;
            return Task.FromResult(AuthenticatorLinker.FinalizeResult.Success);
        });
        Assert.False(flow.ConfirmRecoveryCode("R12345"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => flow.FinalizeAsync("12345"));
        await flow.SaveAsync();
        Assert.False(flow.ConfirmRecoveryCode("wrong"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => flow.FinalizeAsync("12345"));
        Assert.Equal(0, requests);
        Assert.True(flow.ConfirmRecoveryCode(" r12345 "));
        await flow.FinalizeAsync("12345");
        Assert.Equal(EnrollmentState.Completed, flow.State);
        Assert.Equal(1, requests);
    }

    [Fact]
    public async Task InitialSaveFailureBlocksFinalization()
    {
        var flow = new AuthenticatorEnrollment(Account(), _ => throw new IOException(),
            _ => throw new Exception("Must not contact Steam"));
        await Assert.ThrowsAsync<IOException>(() => flow.SaveAsync());
        Assert.Equal(EnrollmentState.PendingSave, flow.State);
        Assert.False(flow.ConfirmRecoveryCode("R12345"));
    }

    [Fact]
    public async Task FinalSaveRetryDoesNotRepeatSteamRequest()
    {
        int saves = 0, requests = 0;
        var flow = new AuthenticatorEnrollment(Account(), _ =>
        {
            if (++saves == 3) throw new IOException();
            return Task.CompletedTask;
        }, _ =>
        {
            requests++;
            return Task.FromResult(AuthenticatorLinker.FinalizeResult.Success);
        });
        await flow.SaveAsync();
        flow.ConfirmRecoveryCode("R12345");
        await Assert.ThrowsAsync<IOException>(() => flow.FinalizeAsync("12345"));
        Assert.Equal(EnrollmentState.FinalSaveRequired, flow.State);
        await flow.SaveAsync();
        Assert.Equal(EnrollmentState.Completed, flow.State);
        Assert.Equal(1, requests);
    }

    [Fact]
    public async Task LostSteamResponseIsNotReportedAsSuccessOrAutomaticallyRetried()
    {
        var flow = new AuthenticatorEnrollment(Account(), _ => Task.CompletedTask,
            _ => throw new HttpRequestException());
        await flow.SaveAsync();
        flow.ConfirmRecoveryCode("R12345");
        await Assert.ThrowsAsync<HttpRequestException>(() => flow.FinalizeAsync("12345"));
        Assert.Equal(EnrollmentState.OutcomeUnknown, flow.State);
        await Assert.ThrowsAsync<InvalidOperationException>(() => flow.FinalizeAsync("12345"));
    }

    [Fact]
    public async Task WrongSmsAllowsExplicitRetry()
    {
        var flow = new AuthenticatorEnrollment(Account(), _ => Task.CompletedTask,
            _ => Task.FromResult(AuthenticatorLinker.FinalizeResult.BadSMSCode));
        await flow.SaveAsync();
        flow.ConfirmRecoveryCode("R12345");
        await flow.FinalizeAsync("12345");
        Assert.Equal(EnrollmentState.ReadyToFinalize, flow.State);
    }

    [Fact]
    public async Task ConcurrentSubmissionCannotSendTwoRequests()
    {
        var response = new TaskCompletionSource<AuthenticatorLinker.FinalizeResult>();
        var flow = new AuthenticatorEnrollment(Account(), _ => Task.CompletedTask, _ => response.Task);
        await flow.SaveAsync();
        flow.ConfirmRecoveryCode("R12345");
        Task first = flow.FinalizeAsync("12345");
        await Assert.ThrowsAsync<InvalidOperationException>(() => flow.FinalizeAsync("12345"));
        response.SetResult(AuthenticatorLinker.FinalizeResult.Success);
        await first;
        Assert.Equal(EnrollmentState.Completed, flow.State);
    }
}
