using CodexSwitcher.Core.Profiles;

namespace CodexSwitcher.Tests.Profiles;

[TestClass]
public sealed class SwitchProfileUseCaseTests
{
    [TestMethod]
    public async Task Execute_FromRunningKnownProfile_ForceStopsAppliesTargetAndRuns()
    {
        var fixture = new Fixture();

        var result = await fixture.UseCase.ExecuteAsync(
            fixture.TargetProfile.Id,
            CancellationToken.None);

        Assert.AreEqual(SwitchProfileStatus.Switched, result.Status);
        CollectionAssert.AreEqual(
            fixture.TargetCredential,
            fixture.Authentication.CurrentCredential);
        Assert.IsFalse(fixture.Authentication.HasRecovery);
        CollectionAssert.Contains(
            fixture.Events,
            "codex:force-stop");
        CollectionAssert.Contains(
            fixture.Events,
            "auth:prepare-profile");
        Assert.AreEqual(1, fixture.Codex.ForceStopCount);
        CollectionAssert.DoesNotContain(
            fixture.Events,
            "codex:request-stop");
        Assert.IsLessThan(
            fixture.Events.IndexOf("auth:prepare-profile"),
            fixture.Events.IndexOf("codex:force-stop"));
        Assert.IsLessThan(
            fixture.Events.IndexOf("codex:launch"),
            fixture.Events.IndexOf("auth:prepare-profile"));
    }

    [TestMethod]
    public async Task Execute_WhenTargetAlreadyRuns_DoesNotRestartOrRewriteAuthentication()
    {
        var fixture = new Fixture();
        fixture.Authentication.CurrentCredential =
            fixture.TargetCredential.ToArray();

        var result = await fixture.UseCase.ExecuteAsync(
            fixture.TargetProfile.Id,
            CancellationToken.None);

        Assert.AreEqual(
            SwitchProfileStatus.AlreadyRunningTarget,
            result.Status);
        CollectionAssert.DoesNotContain(
            fixture.Events,
            "codex:request-stop");
        CollectionAssert.DoesNotContain(
            fixture.Events,
            "auth:prepare-profile");
    }

    [TestMethod]
    public async Task Execute_WhenCurrentRunningProfileIsUnknown_DoesNotSwitch()
    {
        var fixture = new Fixture();
        fixture.Authentication.CurrentCredential =
            "unknown"u8.ToArray();

        var result = await fixture.UseCase.ExecuteAsync(
            fixture.TargetProfile.Id,
            CancellationToken.None);

        Assert.AreEqual(
            SwitchProfileStatus.RunningUnknownProfile,
            result.Status);
        CollectionAssert.DoesNotContain(
            fixture.Events,
            "codex:request-stop");
        CollectionAssert.DoesNotContain(
            fixture.Events,
            "auth:prepare-profile");
    }

    [TestMethod]
    public async Task Execute_WhenForceStopFails_DoesNotChangeAuthentication()
    {
        var fixture = new Fixture();
        fixture.Codex.ForceStopResult = false;

        var result = await fixture.UseCase.ExecuteAsync(
            fixture.TargetProfile.Id,
            CancellationToken.None);

        Assert.AreEqual(
            SwitchProfileStatus.Failed,
            result.Status);
        Assert.AreEqual(1, fixture.Codex.ForceStopCount);
        CollectionAssert.AreEqual(
            fixture.ActiveCredential,
            fixture.Authentication.CurrentCredential);
        CollectionAssert.DoesNotContain(
            fixture.Events,
            "auth:prepare-profile");
        CollectionAssert.DoesNotContain(
            fixture.Events,
            "codex:launch");
    }

    [TestMethod]
    public async Task Execute_WhenTargetProfileIsMissing_DoesNotChangeAuthentication()
    {
        var fixture = new Fixture();

        var result = await fixture.UseCase.ExecuteAsync(
            ProfileId.New(),
            CancellationToken.None);

        Assert.AreEqual(
            SwitchProfileStatus.ProfileNotFound,
            result.Status);
        CollectionAssert.AreEqual(
            fixture.ActiveCredential,
            fixture.Authentication.CurrentCredential);
        CollectionAssert.DoesNotContain(
            fixture.Events,
            "codex:request-stop");
    }

    [TestMethod]
    public async Task Execute_WhenLaunchFails_RestoresPreviousAuthentication()
    {
        var fixture = new Fixture();
        fixture.Codex.LaunchStatus =
            CodexLaunchStatus.Failed;

        var result = await fixture.UseCase.ExecuteAsync(
            fixture.TargetProfile.Id,
            CancellationToken.None);

        Assert.AreEqual(
            SwitchProfileStatus.LaunchFailed,
            result.Status);
        CollectionAssert.AreEqual(
            fixture.ActiveCredential,
            fixture.Authentication.CurrentCredential);
        Assert.IsFalse(fixture.Authentication.HasRecovery);
    }

    [TestMethod]
    public async Task Execute_WhenAppliedCredentialDoesNotMatch_RestoresPreviousAuthentication()
    {
        var fixture = new Fixture();
        fixture.Authentication.ReplaceAppliedCredential =
            "different"u8.ToArray();

        var result = await fixture.UseCase.ExecuteAsync(
            fixture.TargetProfile.Id,
            CancellationToken.None);

        Assert.AreEqual(
            SwitchProfileStatus.AuthenticationMismatch,
            result.Status);
        CollectionAssert.AreEqual(
            fixture.ActiveCredential,
            fixture.Authentication.CurrentCredential);
        CollectionAssert.DoesNotContain(
            fixture.Events,
            "codex:launch");
    }

    [TestMethod]
    public async Task Execute_WhenRecoveryIsPending_BlocksSwitch()
    {
        var fixture = new Fixture();
        fixture.Authentication.HasRecovery = true;

        var result = await fixture.UseCase.ExecuteAsync(
            fixture.TargetProfile.Id,
            CancellationToken.None);

        Assert.AreEqual(
            SwitchProfileStatus.RecoveryRequired,
            result.Status);
        CollectionAssert.AreEqual(
            new[] { "auth:has-recovery" },
            fixture.Events);
    }

    private sealed class Fixture
    {
        public Fixture()
        {
            ActiveProfile = new Profile(
                ProfileId.New(),
                ProfileName.Create("Work"));
            TargetProfile = new Profile(
                ProfileId.New(),
                ProfileName.Create("Personal"));
            Store = new StubProfileStore();
            Store.Credentials[ActiveProfile.Id] =
                ActiveCredential.ToArray();
            Store.Credentials[TargetProfile.Id] =
                TargetCredential.ToArray();
            Store.Profiles.Add(ActiveProfile);
            Store.Profiles.Add(TargetProfile);
            Authentication = new StubAuthenticationSession(
                Events,
                ActiveCredential);
            Codex = new StubCodexController(Events);
            UseCase = new SwitchProfileUseCase(
                Store,
                Authentication,
                Codex,
                new ProfileOperationCoordinator());
        }

        public byte[] ActiveCredential { get; } =
            "active"u8.ToArray();

        public byte[] TargetCredential { get; } =
            "target"u8.ToArray();

        public List<string> Events { get; } = [];

        public Profile ActiveProfile { get; }

        public Profile TargetProfile { get; }

        public StubProfileStore Store { get; }

        public StubAuthenticationSession Authentication { get; }

        public StubCodexController Codex { get; }

        public SwitchProfileUseCase UseCase { get; }
    }

    private sealed class StubProfileStore : IProfileStore
    {
        public List<Profile> Profiles { get; } = [];

        public Dictionary<ProfileId, byte[]> Credentials { get; } = [];

        public Task<ProfileStoreReadResult> ReadAllAsync(
            CancellationToken cancellationToken)
        {
            return Task.FromResult(
                new ProfileStoreReadResult(Profiles.ToArray(), []));
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
            if (!Credentials.TryGetValue(
                    profileId,
                    out var credential))
            {
                throw new DirectoryNotFoundException();
            }

            return Task.FromResult(credential.ToArray());
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

        public StubAuthenticationSession(
            List<string> events,
            byte[] previousCredential)
        {
            _events = events;
            _previousCredential = previousCredential.ToArray();
            CurrentCredential = previousCredential.ToArray();
        }

        public bool HasRecovery { get; set; }

        public byte[] CurrentCredential { get; set; }

        public byte[]? ReplaceAppliedCredential { get; set; }

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

        public Task PrepareForProfileAsync(
            ReadOnlyMemory<byte> credential,
            CancellationToken cancellationToken)
        {
            _events.Add("auth:prepare-profile");
            HasRecovery = true;
            CurrentCredential =
                ReplaceAppliedCredential?.ToArray() ??
                credential.ToArray();
            return Task.CompletedTask;
        }

        public Task<byte[]?> ReadCurrentCredentialAsync(
            CancellationToken cancellationToken)
        {
            _events.Add("auth:read-current");
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

        public bool IsRunning { get; set; } = true;

        public bool WaitForRunning { get; set; } = true;

        public CodexLaunchStatus LaunchStatus { get; set; } =
            CodexLaunchStatus.Launched;

        public CodexStopStatus StopStatus { get; set; } =
            CodexStopStatus.Stopped;

        public bool ForceStopResult { get; set; } = true;

        public int ForceStopCount { get; private set; }

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
            _events.Add("codex:force-stop");
            ForceStopCount++;
            return Task.FromResult(ForceStopResult);
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
