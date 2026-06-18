using CodexSwitcher.Core.Profiles;

namespace CodexSwitcher.Tests.Profiles;

[TestClass]
public sealed class RunProfileUseCaseTests
{
    [TestMethod]
    public async Task Execute_FromStoppedState_AppliesCredentialAndRuns()
    {
        var fixture = new Fixture();

        var result = await fixture.UseCase.ExecuteAsync(
            fixture.Profile.Id,
            CancellationToken.None);

        Assert.AreEqual(RunProfileStatus.Running, result.Status);
        CollectionAssert.AreEqual(
            fixture.TargetCredential,
            fixture.Authentication.CurrentCredential);
        Assert.IsFalse(fixture.Authentication.HasRecovery);
        CollectionAssert.AreEqual(
            new[]
            {
                "auth:has-recovery",
                "codex:is-running",
                "auth:prepare-profile",
                "auth:read-current",
                "codex:launch",
                "codex:wait-running",
                "auth:read-current",
                "auth:clear"
            },
            fixture.Events);
    }

    [TestMethod]
    public async Task Execute_WhenCodexAlreadyRuns_DoesNotChangeAuthentication()
    {
        var fixture = new Fixture();
        fixture.Codex.IsRunning = true;

        var result = await fixture.UseCase.ExecuteAsync(
            fixture.Profile.Id,
            CancellationToken.None);

        Assert.AreEqual(
            RunProfileStatus.AlreadyRunning,
            result.Status);
        CollectionAssert.AreEqual(
            new[]
            {
                "auth:has-recovery",
                "codex:is-running"
            },
            fixture.Events);
    }

    [TestMethod]
    public async Task Execute_WhenRecoveryIsPending_BlocksRun()
    {
        var fixture = new Fixture();
        fixture.Authentication.HasRecovery = true;

        var result = await fixture.UseCase.ExecuteAsync(
            fixture.Profile.Id,
            CancellationToken.None);

        Assert.AreEqual(
            RunProfileStatus.RecoveryRequired,
            result.Status);
        CollectionAssert.AreEqual(
            new[] { "auth:has-recovery" },
            fixture.Events);
    }

    [TestMethod]
    public async Task Execute_WhenProfileIsMissing_DoesNotChangeAuthentication()
    {
        var fixture = new Fixture();

        var result = await fixture.UseCase.ExecuteAsync(
            ProfileId.New(),
            CancellationToken.None);

        Assert.AreEqual(
            RunProfileStatus.ProfileNotFound,
            result.Status);
        Assert.IsFalse(fixture.Authentication.HasRecovery);
    }

    [TestMethod]
    public async Task Execute_WhenInstallationIsMissing_RestoresPreviousCredential()
    {
        var fixture = new Fixture();
        fixture.Codex.LaunchStatus =
            CodexLaunchStatus.InstallationNotFound;

        var result = await fixture.UseCase.ExecuteAsync(
            fixture.Profile.Id,
            CancellationToken.None);

        Assert.AreEqual(
            RunProfileStatus.InstallationNotFound,
            result.Status);
        CollectionAssert.AreEqual(
            fixture.PreviousCredential,
            fixture.Authentication.CurrentCredential);
        Assert.IsFalse(fixture.Authentication.HasRecovery);
        CollectionAssert.Contains(
            fixture.Events,
            "auth:restore");
    }

    [TestMethod]
    public async Task Execute_WhenLaunchFails_RestoresPreviousCredential()
    {
        var fixture = new Fixture();
        fixture.Codex.LaunchStatus =
            CodexLaunchStatus.Failed;

        var result = await fixture.UseCase.ExecuteAsync(
            fixture.Profile.Id,
            CancellationToken.None);

        Assert.AreEqual(
            RunProfileStatus.LaunchFailed,
            result.Status);
        CollectionAssert.AreEqual(
            fixture.PreviousCredential,
            fixture.Authentication.CurrentCredential);
        Assert.IsFalse(fixture.Authentication.HasRecovery);
    }

    [TestMethod]
    public async Task Execute_WhenProcessDoesNotStart_RestoresPreviousCredential()
    {
        var fixture = new Fixture();
        fixture.Codex.WaitForRunning = false;

        var result = await fixture.UseCase.ExecuteAsync(
            fixture.Profile.Id,
            CancellationToken.None);

        Assert.AreEqual(
            RunProfileStatus.LaunchFailed,
            result.Status);
        CollectionAssert.AreEqual(
            fixture.PreviousCredential,
            fixture.Authentication.CurrentCredential);
    }

    [TestMethod]
    public async Task Execute_WhenAppliedCredentialDoesNotMatch_DoesNotLaunch()
    {
        var fixture = new Fixture();
        fixture.Authentication.ReplaceAppliedCredential =
            "different"u8.ToArray();

        var result = await fixture.UseCase.ExecuteAsync(
            fixture.Profile.Id,
            CancellationToken.None);

        Assert.AreEqual(
            RunProfileStatus.AuthenticationMismatch,
            result.Status);
        CollectionAssert.DoesNotContain(
            fixture.Events,
            "codex:launch");
        CollectionAssert.AreEqual(
            fixture.PreviousCredential,
            fixture.Authentication.CurrentCredential);
    }

    [TestMethod]
    public async Task Execute_WhenCredentialChangesAfterLaunch_StopsAndRestores()
    {
        var fixture = new Fixture();
        fixture.Authentication.ChangeAfterFirstRead =
            "changed-after-launch"u8.ToArray();

        var result = await fixture.UseCase.ExecuteAsync(
            fixture.Profile.Id,
            CancellationToken.None);

        Assert.AreEqual(
            RunProfileStatus.AuthenticationMismatch,
            result.Status);
        CollectionAssert.Contains(
            fixture.Events,
            "codex:request-stop");
        CollectionAssert.AreEqual(
            fixture.PreviousCredential,
            fixture.Authentication.CurrentCredential);
    }

    [TestMethod]
    public async Task Execute_WhenMismatchCannotStop_LeavesRecoveryPending()
    {
        var fixture = new Fixture();
        fixture.Authentication.ChangeAfterFirstRead =
            "changed-after-launch"u8.ToArray();
        fixture.Codex.StopStatus =
            CodexStopStatus.ForceCloseRequired;

        var result = await fixture.UseCase.ExecuteAsync(
            fixture.Profile.Id,
            CancellationToken.None);

        Assert.AreEqual(
            RunProfileStatus.RecoveryRequired,
            result.Status);
        Assert.IsTrue(fixture.Authentication.HasRecovery);
        CollectionAssert.DoesNotContain(
            fixture.Events,
            "auth:restore");
    }

    [TestMethod]
    public async Task Execute_WhileAnotherRunIsInProgress_RejectsDuplicateRun()
    {
        var fixture = new Fixture();
        fixture.Authentication.PrepareBlocker =
            new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
        var first = fixture.UseCase.ExecuteAsync(
            fixture.Profile.Id,
            CancellationToken.None);
        await fixture.Authentication.PrepareEntered.Task;

        var second = await fixture.UseCase.ExecuteAsync(
            fixture.Profile.Id,
            CancellationToken.None);
        fixture.Authentication.PrepareBlocker.SetResult();
        var firstResult = await first;

        Assert.AreEqual(
            RunProfileStatus.Failed,
            second.Status);
        Assert.AreEqual(
            RunProfileStatus.Running,
            firstResult.Status);
    }

    [TestMethod]
    public async Task Execute_WhenRecoveryCleanupFailsAfterLaunch_DoesNotRewriteRunningAuthentication()
    {
        var fixture = new Fixture();
        fixture.Authentication.ThrowOnClear = true;

        var result = await fixture.UseCase.ExecuteAsync(
            fixture.Profile.Id,
            CancellationToken.None);

        Assert.AreEqual(
            RunProfileStatus.RecoveryRequired,
            result.Status);
        CollectionAssert.AreEqual(
            fixture.TargetCredential,
            fixture.Authentication.CurrentCredential);
        Assert.IsTrue(fixture.Authentication.HasRecovery);
        CollectionAssert.DoesNotContain(
            fixture.Events,
            "auth:restore");
    }

    private sealed class Fixture
    {
        public Fixture()
        {
            Profile = new Profile(
                ProfileId.New(),
                ProfileName.Create("Work"));
            Store = new StubProfileStore(Profile, TargetCredential);
            Authentication = new StubAuthenticationSession(
                Events,
                PreviousCredential);
            Codex = new StubCodexController(Events);
            UseCase = new RunProfileUseCase(
                Store,
                Authentication,
                Codex,
                new ProfileOperationCoordinator());
        }

        public byte[] PreviousCredential { get; } =
            "previous"u8.ToArray();

        public byte[] TargetCredential { get; } =
            "target"u8.ToArray();

        public List<string> Events { get; } = [];

        public Profile Profile { get; }

        public StubProfileStore Store { get; }

        public StubAuthenticationSession Authentication { get; }

        public StubCodexController Codex { get; }

        public RunProfileUseCase UseCase { get; }
    }

    private sealed class StubProfileStore : IProfileStore
    {
        private readonly Profile _profile;
        private readonly byte[] _credential;

        public StubProfileStore(
            Profile profile,
            byte[] credential)
        {
            _profile = profile;
            _credential = credential;
        }

        public Task<ProfileStoreReadResult> ReadAllAsync(
            CancellationToken cancellationToken)
        {
            return Task.FromResult(
                new ProfileStoreReadResult([_profile], []));
        }

        public Task SaveAsync(
            Profile profile,
            ReadOnlyMemory<byte> credential,
            CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task<byte[]> ReadCredentialAsync(
            ProfileId profileId,
            CancellationToken cancellationToken)
        {
            if (profileId != _profile.Id)
            {
                throw new DirectoryNotFoundException();
            }

            return Task.FromResult(_credential.ToArray());
        }

        public Task DeleteAsync(
            ProfileId profileId,
            CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }
    }

    private sealed class StubAuthenticationSession
        : IAuthenticationSession
    {
        private readonly List<string> _events;
        private readonly byte[] _previousCredential;
        private int _readCount;

        public StubAuthenticationSession(
            List<string> events,
            byte[] previousCredential)
        {
            _events = events;
            _previousCredential = previousCredential.ToArray();
            CurrentCredential = previousCredential.ToArray();
        }

        public bool HasRecovery { get; set; }

        public byte[] CurrentCredential { get; private set; }

        public byte[]? ReplaceAppliedCredential { get; set; }

        public byte[]? ChangeAfterFirstRead { get; set; }

        public TaskCompletionSource? PrepareBlocker { get; set; }

        public TaskCompletionSource PrepareEntered { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool ThrowOnClear { get; set; }

        public Task<bool> HasPendingRecoveryAsync(
            CancellationToken cancellationToken)
        {
            _events.Add("auth:has-recovery");
            return Task.FromResult(HasRecovery);
        }

        public Task PrepareForLoginAsync(
            CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public async Task PrepareForProfileAsync(
            ReadOnlyMemory<byte> credential,
            CancellationToken cancellationToken)
        {
            _events.Add("auth:prepare-profile");
            PrepareEntered.TrySetResult();
            if (PrepareBlocker is not null)
            {
                await PrepareBlocker.Task;
            }

            HasRecovery = true;
            CurrentCredential =
                ReplaceAppliedCredential?.ToArray() ??
                credential.ToArray();
        }

        public Task<byte[]?> ReadCurrentCredentialAsync(
            CancellationToken cancellationToken)
        {
            _events.Add("auth:read-current");
            _readCount++;
            if (_readCount > 1 &&
                ChangeAfterFirstRead is not null)
            {
                CurrentCredential =
                    ChangeAfterFirstRead.ToArray();
            }

            return Task.FromResult<byte[]?>(
                CurrentCredential.ToArray());
        }

        public Task RestorePreviousAsync(
            CancellationToken cancellationToken)
        {
            _events.Add("auth:restore");
            CurrentCredential =
                _previousCredential.ToArray();
            return Task.CompletedTask;
        }

        public Task ClearRecoveryAsync(
            CancellationToken cancellationToken)
        {
            _events.Add("auth:clear");
            if (ThrowOnClear)
            {
                throw new IOException("복구 정리 실패");
            }

            HasRecovery = false;
            return Task.CompletedTask;
        }
    }

    private sealed class StubCodexController : ICodexLoginController
    {
        private readonly List<string> _events;

        public StubCodexController(List<string> events)
        {
            _events = events;
        }

        public bool IsRunning { get; set; }

        public bool WaitForRunning { get; set; } = true;

        public CodexLaunchStatus LaunchStatus { get; set; } =
            CodexLaunchStatus.Launched;

        public CodexStopStatus StopStatus { get; set; } =
            CodexStopStatus.Stopped;

        public Task<bool> IsRunningAsync(
            CancellationToken cancellationToken)
        {
            _events.Add("codex:is-running");
            return Task.FromResult(IsRunning);
        }

        public Task<CodexStopStatus> RequestStopAsync(
            CancellationToken cancellationToken)
        {
            _events.Add("codex:request-stop");
            return Task.FromResult(StopStatus);
        }

        public Task<bool> ForceStopAsync(
            CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task<CodexLaunchStatus> LaunchAsync(
            CancellationToken cancellationToken)
        {
            _events.Add("codex:launch");
            return Task.FromResult(LaunchStatus);
        }

        public Task<bool> WaitForRunningAsync(
            CancellationToken cancellationToken)
        {
            _events.Add("codex:wait-running");
            return Task.FromResult(WaitForRunning);
        }
    }
}
