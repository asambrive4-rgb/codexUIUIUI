using CodexSwitcher.Core.Profiles;

namespace CodexSwitcher.Tests.Profiles;

[TestClass]
public sealed class DeleteProfileUseCaseTests
{
    [TestMethod]
    public async Task Execute_WhenCodexIsStopped_DeletesSelectedProfileOnly()
    {
        var fixture = new Fixture();
        fixture.Codex.IsRunning = false;

        var result = await fixture.UseCase.ExecuteAsync(
            fixture.TargetProfile.Id,
            CancellationToken.None);

        Assert.AreEqual(DeleteProfileStatus.Deleted, result.Status);
        Assert.IsTrue(
            fixture.Store.DeleteCalls.SequenceEqual(
                [fixture.TargetProfile.Id]));
        Assert.IsFalse(
            fixture.Store.Profiles.Any(
                profile => profile.Id == fixture.TargetProfile.Id));
        Assert.IsTrue(
            fixture.Store.Profiles.Any(
                profile => profile.Id == fixture.ActiveProfile.Id));
        Assert.IsFalse(fixture.Store.CommonDataTouched);
    }

    [TestMethod]
    public async Task Execute_WhenCodexRunsDifferentProfile_DeletesTarget()
    {
        var fixture = new Fixture();

        var result = await fixture.UseCase.ExecuteAsync(
            fixture.TargetProfile.Id,
            CancellationToken.None);

        Assert.AreEqual(DeleteProfileStatus.Deleted, result.Status);
        CollectionAssert.AreEqual(
            new[] { fixture.TargetProfile.Id },
            fixture.Store.DeleteCalls);
    }

    [TestMethod]
    public async Task Execute_WhenTargetProfileIsActive_BlocksDelete()
    {
        var fixture = new Fixture();

        var result = await fixture.UseCase.ExecuteAsync(
            fixture.ActiveProfile.Id,
            CancellationToken.None);

        Assert.AreEqual(
            DeleteProfileStatus.ActiveProfileBlocked,
            result.Status);
        Assert.IsEmpty(fixture.Store.DeleteCalls);
        Assert.IsTrue(
            fixture.Store.Profiles.Any(
                profile => profile.Id == fixture.ActiveProfile.Id));
    }

    [TestMethod]
    public async Task Execute_WhenRunningProfileIsUnknown_BlocksDelete()
    {
        var fixture = new Fixture();
        fixture.Authentication.CurrentCredential =
            "unknown"u8.ToArray();

        var result = await fixture.UseCase.ExecuteAsync(
            fixture.TargetProfile.Id,
            CancellationToken.None);

        Assert.AreEqual(
            DeleteProfileStatus.RunningProfileUnknown,
            result.Status);
        Assert.IsEmpty(fixture.Store.DeleteCalls);
    }

    [TestMethod]
    public async Task Execute_WhenCurrentCredentialMatchesMultipleProfiles_BlocksDelete()
    {
        var fixture = new Fixture();
        fixture.Store.Credentials[fixture.TargetProfile.Id] =
            fixture.ActiveCredential.ToArray();

        var result = await fixture.UseCase.ExecuteAsync(
            fixture.TargetProfile.Id,
            CancellationToken.None);

        Assert.AreEqual(
            DeleteProfileStatus.RunningProfileUnknown,
            result.Status);
        Assert.IsEmpty(fixture.Store.DeleteCalls);
    }

    [TestMethod]
    public async Task Execute_WhenRecoveryIsPending_BlocksDelete()
    {
        var fixture = new Fixture();
        fixture.Authentication.HasRecovery = true;

        var result = await fixture.UseCase.ExecuteAsync(
            fixture.TargetProfile.Id,
            CancellationToken.None);

        Assert.AreEqual(
            DeleteProfileStatus.RecoveryRequired,
            result.Status);
        Assert.IsEmpty(fixture.Store.DeleteCalls);
        Assert.IsEmpty(fixture.Events.Where(
            item => item.StartsWith(
                "codex:",
                StringComparison.Ordinal)).ToArray());
    }

    [TestMethod]
    public async Task Execute_WhenProfileIsMissing_DoesNotDelete()
    {
        var fixture = new Fixture();

        var result = await fixture.UseCase.ExecuteAsync(
            ProfileId.New(),
            CancellationToken.None);

        Assert.AreEqual(
            DeleteProfileStatus.ProfileNotFound,
            result.Status);
        Assert.IsEmpty(fixture.Store.DeleteCalls);
    }

    [TestMethod]
    public async Task Execute_WhenStoreDeleteFails_KeepsProfileInList()
    {
        var fixture = new Fixture();
        fixture.Store.ThrowOnDelete = true;

        var result = await fixture.UseCase.ExecuteAsync(
            fixture.TargetProfile.Id,
            CancellationToken.None);

        Assert.AreEqual(DeleteProfileStatus.Failed, result.Status);
        CollectionAssert.AreEqual(
            new[] { fixture.TargetProfile.Id },
            fixture.Store.DeleteCalls);
        Assert.IsTrue(
            fixture.Store.Profiles.Any(
                profile => profile.Id == fixture.TargetProfile.Id));
    }

    [TestMethod]
    public async Task Execute_WhileAnotherOperationIsInProgress_RejectsDuplicateDelete()
    {
        var fixture = new Fixture();
        fixture.Codex.IsRunning = false;
        fixture.Store.DeleteBlocker =
            new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
        var first = fixture.UseCase.ExecuteAsync(
            fixture.TargetProfile.Id,
            CancellationToken.None);
        await fixture.Store.DeleteEntered.Task;

        var second = await fixture.UseCase.ExecuteAsync(
            fixture.ActiveProfile.Id,
            CancellationToken.None);
        fixture.Store.DeleteBlocker.SetResult();
        var firstResult = await first;

        Assert.AreEqual(DeleteProfileStatus.Failed, second.Status);
        Assert.AreEqual(DeleteProfileStatus.Deleted, firstResult.Status);
        CollectionAssert.AreEqual(
            new[] { fixture.TargetProfile.Id },
            fixture.Store.DeleteCalls);
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
            Store.Profiles.Add(ActiveProfile);
            Store.Profiles.Add(TargetProfile);
            Store.Credentials[ActiveProfile.Id] =
                ActiveCredential.ToArray();
            Store.Credentials[TargetProfile.Id] =
                TargetCredential.ToArray();
            Authentication = new StubAuthenticationSession(
                Events,
                ActiveCredential);
            Codex = new StubCodexController(Events);
            UseCase = new DeleteProfileUseCase(
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

        public DeleteProfileUseCase UseCase { get; }
    }

    private sealed class StubProfileStore : IProfileStore
    {
        public List<Profile> Profiles { get; } = [];

        public Dictionary<ProfileId, byte[]> Credentials { get; } = [];

        public List<ProfileId> DeleteCalls { get; } = [];

        public bool ThrowOnDelete { get; set; }

        public bool CommonDataTouched { get; private set; }

        public TaskCompletionSource? DeleteBlocker { get; set; }

        public TaskCompletionSource DeleteEntered { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<ProfileStoreReadResult> ReadAllAsync(
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
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
            throw new NotSupportedException();
        }

        public Task<byte[]> ReadCredentialAsync(
            ProfileId profileId,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!Credentials.TryGetValue(
                    profileId,
                    out var credential))
            {
                throw new DirectoryNotFoundException();
            }

            return Task.FromResult(credential.ToArray());
        }

        public async Task DeleteAsync(
            ProfileId profileId,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            DeleteCalls.Add(profileId);
            DeleteEntered.TrySetResult();
            if (DeleteBlocker is not null)
            {
                await DeleteBlocker.Task;
            }

            if (ThrowOnDelete)
            {
                throw new IOException("삭제 실패");
            }

            Credentials.Remove(profileId);
            Profiles.RemoveAll(profile => profile.Id == profileId);
        }
    }

    private sealed class StubAuthenticationSession
        : IAuthenticationSession
    {
        private readonly List<string> _events;

        public StubAuthenticationSession(
            List<string> events,
            byte[] currentCredential)
        {
            _events = events;
            CurrentCredential = currentCredential.ToArray();
        }

        public bool HasRecovery { get; set; }

        public byte[] CurrentCredential { get; set; }

        public Task<bool> HasPendingRecoveryAsync(
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
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
            throw new NotSupportedException();
        }

        public Task<byte[]?> ReadCurrentCredentialAsync(
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _events.Add("auth:read-current");
            return Task.FromResult<byte[]?>(
                CurrentCredential.ToArray());
        }

        public Task RestorePreviousAsync(
            CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task ClearRecoveryAsync(
            CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
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

        public Task<bool> IsRunningAsync(
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _events.Add("codex:is-running");
            return Task.FromResult(IsRunning);
        }

        public Task<CodexStopStatus> RequestStopAsync(
            CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task<bool> ForceStopAsync(
            CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task<CodexLaunchStatus> LaunchAsync(
            CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task<bool> WaitForRunningAsync(
            CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }
    }
}
