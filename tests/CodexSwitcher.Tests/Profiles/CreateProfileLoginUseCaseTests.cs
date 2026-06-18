using CodexSwitcher.Core.Profiles;

namespace CodexSwitcher.Tests.Profiles;

[TestClass]
public sealed class CreateProfileLoginUseCaseTests
{
    [TestMethod]
    public async Task Start_WithInvalidName_DoesNotTouchAuthenticationOrCodex()
    {
        var fixture = new Fixture();

        var result = await fixture.UseCase.StartAsync(
            " ",
            forceCloseApproved: false,
            CancellationToken.None);

        Assert.AreEqual(
            CreateProfileLoginStatus.InvalidName,
            result.Status);
        CollectionAssert.AreEqual(
            new[] { "has-recovery" },
            fixture.Authentication.Calls);
        Assert.IsEmpty(fixture.Codex.Calls);
    }

    [TestMethod]
    public async Task Start_WithDuplicateName_DoesNotTouchAuthenticationOrCodex()
    {
        var fixture = new Fixture();
        fixture.Store.Profiles.Add(
            new Profile(
                ProfileId.New(),
                ProfileName.Create("Work")));

        var result = await fixture.UseCase.StartAsync(
            "work",
            forceCloseApproved: false,
            CancellationToken.None);

        Assert.AreEqual(
            CreateProfileLoginStatus.DuplicateName,
            result.Status);
        CollectionAssert.AreEqual(
            new[] { "has-recovery" },
            fixture.Authentication.Calls);
        Assert.IsEmpty(fixture.Codex.Calls);
    }

    [TestMethod]
    public async Task Start_WhenNormalStopFails_DoesNotForceStopWithoutApproval()
    {
        var fixture = new Fixture();
        fixture.Codex.StopStatus =
            CodexStopStatus.ForceCloseRequired;

        var result = await fixture.UseCase.StartAsync(
            "Work",
            forceCloseApproved: false,
            CancellationToken.None);

        Assert.AreEqual(
            CreateProfileLoginStatus.ForceCloseConfirmationRequired,
            result.Status);
        CollectionAssert.AreEqual(
            new[] { "request-stop" },
            fixture.Codex.Calls);
        CollectionAssert.AreEqual(
            new[] { "has-recovery" },
            fixture.Authentication.Calls);
    }

    [TestMethod]
    public async Task Start_AfterForceApproval_PreparesAndLaunches()
    {
        var fixture = new Fixture();
        fixture.Codex.StopStatus =
            CodexStopStatus.ForceCloseRequired;

        var result = await fixture.UseCase.StartAsync(
            "Work",
            forceCloseApproved: true,
            CancellationToken.None);

        Assert.AreEqual(
            CreateProfileLoginStatus.WaitingForLogin,
            result.Status);
        CollectionAssert.AreEqual(
            new[]
            {
                "request-stop",
                "force-stop",
                "launch"
            },
            fixture.Codex.Calls);
        CollectionAssert.AreEqual(
            new[]
            {
                "has-recovery",
                "prepare"
            },
            fixture.Authentication.Calls);
    }

    [TestMethod]
    public async Task Start_WhenLaunchFails_RestoresPreviousAuthentication()
    {
        var fixture = new Fixture();
        fixture.Codex.LaunchStatus = CodexLaunchStatus.Failed;

        var result = await fixture.UseCase.StartAsync(
            "Work",
            forceCloseApproved: false,
            CancellationToken.None);

        Assert.AreEqual(
            CreateProfileLoginStatus.Failed,
            result.Status);
        CollectionAssert.Contains(
            fixture.Authentication.Calls,
            "restore");
        CollectionAssert.Contains(
            fixture.Authentication.Calls,
            "clear");
        Assert.IsFalse(fixture.Authentication.HasRecovery);
    }

    [TestMethod]
    public async Task Start_WhenInstallationIsMissing_RestoresPreviousAuthentication()
    {
        var fixture = new Fixture();
        fixture.Codex.LaunchStatus =
            CodexLaunchStatus.InstallationNotFound;

        var result = await fixture.UseCase.StartAsync(
            "Work",
            forceCloseApproved: false,
            CancellationToken.None);

        Assert.AreEqual(
            CreateProfileLoginStatus.InstallationNotFound,
            result.Status);
        Assert.IsFalse(fixture.Authentication.HasRecovery);
        CollectionAssert.Contains(
            fixture.Authentication.Calls,
            "restore");
    }

    [TestMethod]
    public async Task Complete_WithCredential_RestoresThenSavesProfile()
    {
        var fixture = new Fixture();
        fixture.Authentication.CurrentCredential =
            "new-credential"u8.ToArray();
        await fixture.StartAsync();

        var result = await fixture.UseCase.CompleteAsync(
            forceCloseApproved: false,
            CancellationToken.None);

        Assert.AreEqual(
            CreateProfileLoginStatus.Created,
            result.Status);
        Assert.IsNotNull(result.Profile);
        Assert.AreEqual("Work", result.Profile.Name.Value);
        Assert.HasCount(1, fixture.Store.SavedProfiles);
        CollectionAssert.AreEqual(
            new[]
            {
                "auth:restore",
                "store:save"
            },
            fixture.Events);
        Assert.IsFalse(fixture.Authentication.HasRecovery);
    }

    [TestMethod]
    public async Task Complete_WithoutCredential_RestoresAndDoesNotSave()
    {
        var fixture = new Fixture();
        fixture.Authentication.CurrentCredential = null;
        await fixture.StartAsync();

        var result = await fixture.UseCase.CompleteAsync(
            forceCloseApproved: false,
            CancellationToken.None);

        Assert.AreEqual(
            CreateProfileLoginStatus.LoginNotCompleted,
            result.Status);
        Assert.IsEmpty(fixture.Store.SavedProfiles);
        Assert.IsFalse(fixture.Authentication.HasRecovery);
        CollectionAssert.Contains(
            fixture.Authentication.Calls,
            "restore");
    }

    [TestMethod]
    public async Task Complete_WhenProfileSaveFails_LeavesPreviousAuthenticationRestored()
    {
        var fixture = new Fixture();
        fixture.Store.ThrowOnSave = true;
        await fixture.StartAsync();

        var result = await fixture.UseCase.CompleteAsync(
            forceCloseApproved: false,
            CancellationToken.None);

        Assert.AreEqual(
            CreateProfileLoginStatus.Failed,
            result.Status);
        Assert.IsEmpty(fixture.Store.SavedProfiles);
        Assert.IsFalse(fixture.Authentication.HasRecovery);
        CollectionAssert.Contains(
            fixture.Authentication.Calls,
            "restore");
        CollectionAssert.Contains(
            fixture.Authentication.Calls,
            "clear");
    }

    [TestMethod]
    public async Task Complete_WhenRecoveryFails_RequiresRecoveryAndDoesNotSave()
    {
        var fixture = new Fixture();
        fixture.Authentication.ThrowOnRestore = true;
        await fixture.StartAsync();

        var result = await fixture.UseCase.CompleteAsync(
            forceCloseApproved: false,
            CancellationToken.None);

        Assert.AreEqual(
            CreateProfileLoginStatus.RecoveryRequired,
            result.Status);
        Assert.IsEmpty(fixture.Store.SavedProfiles);
        Assert.IsTrue(fixture.Authentication.HasRecovery);
    }

    [TestMethod]
    public async Task Cancel_RestoresAndLeavesNoProfile()
    {
        var fixture = new Fixture();
        await fixture.StartAsync();

        var result = await fixture.UseCase.CancelAsync(
            forceCloseApproved: false,
            CancellationToken.None);

        Assert.AreEqual(
            CreateProfileLoginStatus.Canceled,
            result.Status);
        Assert.IsEmpty(fixture.Store.SavedProfiles);
        Assert.IsFalse(fixture.Authentication.HasRecovery);
    }

    [TestMethod]
    public async Task Start_WithPendingRecovery_BlocksNewLogin()
    {
        var fixture = new Fixture();
        fixture.Authentication.HasRecovery = true;

        var result = await fixture.UseCase.StartAsync(
            "Work",
            forceCloseApproved: false,
            CancellationToken.None);

        Assert.AreEqual(
            CreateProfileLoginStatus.RecoveryRequired,
            result.Status);
        Assert.IsEmpty(fixture.Codex.Calls);
    }

    [TestMethod]
    public async Task Cancel_AfterNewUseCaseInstance_RecoversPreviousSession()
    {
        var fixture = new Fixture();
        fixture.Authentication.HasRecovery = true;
        var restarted = new CreateProfileLoginUseCase(
            fixture.Store,
            fixture.Authentication,
            fixture.Codex);

        var result = await restarted.CancelAsync(
            forceCloseApproved: false,
            CancellationToken.None);

        Assert.AreEqual(
            CreateProfileLoginStatus.Canceled,
            result.Status);
        Assert.IsFalse(fixture.Authentication.HasRecovery);
    }

    [TestMethod]
    public async Task ConcurrentStart_IsRejected()
    {
        var fixture = new Fixture();
        fixture.Codex.StopBlocker =
            new TaskCompletionSource<CodexStopStatus>(
                TaskCreationOptions.RunContinuationsAsynchronously);
        var first = fixture.UseCase.StartAsync(
            "Work",
            forceCloseApproved: false,
            CancellationToken.None);
        await fixture.Codex.StopEntered.Task;

        var second = await fixture.UseCase.StartAsync(
            "Personal",
            forceCloseApproved: false,
            CancellationToken.None);
        fixture.Codex.StopBlocker.SetResult(
            CodexStopStatus.Stopped);
        _ = await first;

        Assert.AreEqual(
            CreateProfileLoginStatus.Failed,
            second.Status);
    }

    private sealed class Fixture
    {
        public Fixture()
        {
            Store = new StubProfileStore(Events);
            Authentication = new StubAuthenticationSession(Events);
            Codex = new StubCodexController();
            UseCase = new CreateProfileLoginUseCase(
                Store,
                Authentication,
                Codex);
        }

        public List<string> Events { get; } = [];

        public StubProfileStore Store { get; }

        public StubAuthenticationSession Authentication { get; }

        public StubCodexController Codex { get; }

        public CreateProfileLoginUseCase UseCase { get; }

        public async Task StartAsync()
        {
            var result = await UseCase.StartAsync(
                "Work",
                forceCloseApproved: false,
                CancellationToken.None);
            Assert.AreEqual(
                CreateProfileLoginStatus.WaitingForLogin,
                result.Status);
        }
    }

    private sealed class StubProfileStore : IProfileStore
    {
        private readonly List<string> _events;

        public StubProfileStore(List<string> events)
        {
            _events = events;
        }

        public List<Profile> Profiles { get; } = [];

        public List<Profile> SavedProfiles { get; } = [];

        public bool ThrowOnSave { get; set; }

        public Task<ProfileStoreReadResult> ReadAllAsync(
            CancellationToken cancellationToken)
        {
            return Task.FromResult(
                new ProfileStoreReadResult(
                    Profiles.ToArray(),
                    []));
        }

        public Task SaveAsync(
            Profile profile,
            ReadOnlyMemory<byte> credential,
            CancellationToken cancellationToken)
        {
            if (ThrowOnSave)
            {
                throw new IOException("저장 실패");
            }

            _events.Add("store:save");
            SavedProfiles.Add(profile);
            Profiles.Add(profile);
            return Task.CompletedTask;
        }

        public Task<byte[]> ReadCredentialAsync(
            ProfileId profileId,
            CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
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

        public StubAuthenticationSession(List<string> events)
        {
            _events = events;
        }

        public List<string> Calls { get; } = [];

        public bool HasRecovery { get; set; }

        public byte[]? CurrentCredential { get; set; } =
            "credential"u8.ToArray();

        public bool ThrowOnRestore { get; set; }

        public Task<bool> HasPendingRecoveryAsync(
            CancellationToken cancellationToken)
        {
            Calls.Add("has-recovery");
            return Task.FromResult(HasRecovery);
        }

        public Task PrepareForLoginAsync(
            CancellationToken cancellationToken)
        {
            Calls.Add("prepare");
            HasRecovery = true;
            return Task.CompletedTask;
        }

        public Task PrepareForProfileAsync(
            ReadOnlyMemory<byte> credential,
            CancellationToken cancellationToken)
        {
            Calls.Add("prepare-profile");
            HasRecovery = true;
            return Task.CompletedTask;
        }

        public Task<byte[]?> ReadCurrentCredentialAsync(
            CancellationToken cancellationToken)
        {
            Calls.Add("read-current");
            return Task.FromResult(
                CurrentCredential?.ToArray());
        }

        public Task RestorePreviousAsync(
            CancellationToken cancellationToken)
        {
            Calls.Add("restore");
            _events.Add("auth:restore");
            if (ThrowOnRestore)
            {
                throw new IOException("복구 실패");
            }

            return Task.CompletedTask;
        }

        public Task ClearRecoveryAsync(
            CancellationToken cancellationToken)
        {
            Calls.Add("clear");
            HasRecovery = false;
            return Task.CompletedTask;
        }
    }

    private sealed class StubCodexController
        : ICodexLoginController
    {
        public List<string> Calls { get; } = [];

        public CodexStopStatus StopStatus { get; set; } =
            CodexStopStatus.Stopped;

        public CodexLaunchStatus LaunchStatus { get; set; } =
            CodexLaunchStatus.Launched;

        public TaskCompletionSource<CodexStopStatus>? StopBlocker
        {
            get;
            set;
        }

        public TaskCompletionSource StopEntered { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<bool> IsRunningAsync(
            CancellationToken cancellationToken)
        {
            Calls.Add("is-running");
            return Task.FromResult(false);
        }

        public async Task<CodexStopStatus> RequestStopAsync(
            CancellationToken cancellationToken)
        {
            Calls.Add("request-stop");
            StopEntered.TrySetResult();
            if (StopBlocker is not null)
            {
                return await StopBlocker.Task;
            }

            return StopStatus;
        }

        public Task<bool> ForceStopAsync(
            CancellationToken cancellationToken)
        {
            Calls.Add("force-stop");
            return Task.FromResult(true);
        }

        public Task<CodexLaunchStatus> LaunchAsync(
            CancellationToken cancellationToken)
        {
            Calls.Add("launch");
            return Task.FromResult(LaunchStatus);
        }

        public Task<bool> WaitForRunningAsync(
            CancellationToken cancellationToken)
        {
            Calls.Add("wait-running");
            return Task.FromResult(true);
        }
    }
}
