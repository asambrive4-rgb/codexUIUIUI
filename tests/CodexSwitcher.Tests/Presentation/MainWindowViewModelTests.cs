using CodexSwitcher.Bootstrapper.Presentation;
using CodexSwitcher.Core.Installation;
using CodexSwitcher.Core.Profiles;

namespace CodexSwitcher.Tests.Presentation;

[TestClass]
public sealed class MainWindowViewModelTests
{
    [TestMethod]
    public async Task RunProfile_WhileRunning_BlocksDuplicateAndRestoresBusyState()
    {
        var fixture = new Fixture();
        await fixture.ViewModel.InitializeAsync(CancellationToken.None);
        fixture.Authentication.PrepareBlocker =
            new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);

        var first = fixture.ViewModel.RunProfileAsync(
            fixture.Profile.Id,
            CancellationToken.None);
        await fixture.Authentication.PrepareEntered.Task;

        Assert.IsTrue(fixture.ViewModel.IsOperationInProgress);
        Assert.IsFalse(fixture.ViewModel.CanAddProfile);
        Assert.AreEqual(
            "실행 중...",
            fixture.ViewModel.Profiles.Single().Status);
        Assert.IsFalse(
            fixture.ViewModel.Profiles.Single().IsRunEnabled);
        Assert.IsFalse(
            fixture.ViewModel.Profiles.Single().IsDeleteEnabled);

        var duplicate = await fixture.ViewModel.RunProfileAsync(
            fixture.Profile.Id,
            CancellationToken.None);
        Assert.AreEqual(RunProfileStatus.Failed, duplicate.Status);

        fixture.Authentication.PrepareBlocker.SetResult();
        var result = await first;

        Assert.AreEqual(RunProfileStatus.Running, result.Status);
        Assert.IsFalse(fixture.ViewModel.IsOperationInProgress);
        Assert.AreEqual(
            "Codex를 실행했습니다.",
            fixture.ViewModel.OperationStatusMessage);
        Assert.IsTrue(fixture.ViewModel.Profiles.Single().IsActive);
    }

    [TestMethod]
    public async Task RunProfile_WhenCanceled_RestoresBusyState()
    {
        var fixture = new Fixture();
        await fixture.ViewModel.InitializeAsync(CancellationToken.None);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => fixture.ViewModel.RunProfileAsync(
                fixture.Profile.Id,
                cancellation.Token));

        Assert.IsFalse(fixture.ViewModel.IsOperationInProgress);
        Assert.IsTrue(fixture.ViewModel.CanAddProfile);
    }

    [TestMethod]
    public async Task DeleteProfile_Success_RemovesItemFromList()
    {
        var fixture = new Fixture();
        await fixture.ViewModel.InitializeAsync(CancellationToken.None);

        var result = await fixture.ViewModel.DeleteProfileAsync(
            fixture.Profile.Id,
            CancellationToken.None);

        Assert.AreEqual(DeleteProfileStatus.Deleted, result.Status);
        Assert.IsEmpty(fixture.ViewModel.Profiles);
        Assert.AreEqual(
            "프로필을 삭제했습니다.",
            fixture.ViewModel.OperationStatusMessage);
    }

    [TestMethod]
    public async Task DeleteProfile_Failure_KeepsItemInList()
    {
        var fixture = new Fixture();
        fixture.Store.ThrowOnDelete = true;
        await fixture.ViewModel.InitializeAsync(CancellationToken.None);

        var result = await fixture.ViewModel.DeleteProfileAsync(
            fixture.Profile.Id,
            CancellationToken.None);

        Assert.AreEqual(DeleteProfileStatus.Failed, result.Status);
        Assert.HasCount(1, fixture.ViewModel.Profiles);
        Assert.AreEqual(
            "프로필 삭제에 실패했습니다. 목록은 변경하지 않았습니다.",
            fixture.ViewModel.OperationStatusMessage);
    }

    [TestMethod]
    public async Task RefreshRuntimeState_WithRecovery_DisablesActions()
    {
        var fixture = new Fixture();
        fixture.Authentication.HasRecovery = true;

        await fixture.ViewModel.InitializeAsync(CancellationToken.None);

        Assert.IsTrue(fixture.ViewModel.HasPendingRecovery);
        Assert.IsFalse(fixture.ViewModel.CanAddProfile);
        Assert.IsFalse(
            fixture.ViewModel.Profiles.Single().IsRunEnabled);
        Assert.IsFalse(
            fixture.ViewModel.Profiles.Single().IsDeleteEnabled);
    }

    private sealed class Fixture
    {
        public Fixture()
        {
            Store.Profiles.Add(Profile);
            Store.Credentials[Profile.Id] =
                TargetCredential.ToArray();
            Authentication.CurrentCredential =
                PreviousCredential.ToArray();
            var coordinator = new ProfileOperationCoordinator();
            var listProfiles = new ListProfilesUseCase(Store);
            var profileLogin = new CreateProfileLoginUseCase(
                Store,
                Authentication,
                Codex,
                coordinator);
            ViewModel = new MainWindowViewModel(
                new DetectCodexInstallationUseCase(
                    new StubInstallationLocator()),
                listProfiles,
                profileLogin,
                new RunProfileUseCase(
                    Store,
                    Authentication,
                    Codex,
                    coordinator),
                new SwitchProfileUseCase(
                    Store,
                    Authentication,
                    Codex,
                    coordinator),
                new DeleteProfileUseCase(
                    Store,
                    Authentication,
                    Codex,
                    coordinator),
                new GetProfileRuntimeStateUseCase(
                    Store,
                    Authentication,
                    Codex));
        }

        public byte[] PreviousCredential { get; } =
            "previous"u8.ToArray();

        public byte[] TargetCredential { get; } =
            "target"u8.ToArray();

        public Profile Profile { get; } =
            new(
                ProfileId.New(),
                ProfileName.Create("Work"));

        public StubProfileStore Store { get; } = new();

        public StubAuthenticationSession Authentication { get; } =
            new();

        public StubCodexController Codex { get; } = new();

        public MainWindowViewModel ViewModel { get; }
    }

    private sealed class StubInstallationLocator
        : ICodexInstallationLocator
    {
        public Task<bool> IsInstalledAsync(
            CancellationToken cancellationToken) =>
            Task.FromResult(true);
    }

    private sealed class StubProfileStore : IProfileStore
    {
        public List<Profile> Profiles { get; } = [];

        public Dictionary<ProfileId, byte[]> Credentials { get; } = [];

        public bool ThrowOnDelete { get; set; }

        public Task<ProfileStoreReadResult> ReadAllAsync(
            CancellationToken cancellationToken) =>
            Task.FromResult(
                new ProfileStoreReadResult(
                    Profiles.ToArray(),
                    []));

        public Task SaveAsync(
            Profile profile,
            ReadOnlyMemory<byte> credential,
            CancellationToken cancellationToken)
        {
            Profiles.Add(profile);
            Credentials[profile.Id] = credential.ToArray();
            return Task.CompletedTask;
        }

        public Task<byte[]> ReadCredentialAsync(
            ProfileId profileId,
            CancellationToken cancellationToken) =>
            Task.FromResult(
                Credentials[profileId].ToArray());

        public Task DeleteAsync(
            ProfileId profileId,
            CancellationToken cancellationToken)
        {
            if (ThrowOnDelete)
            {
                throw new IOException("delete failed");
            }

            Profiles.RemoveAll(profile => profile.Id == profileId);
            Credentials.Remove(profileId);
            return Task.CompletedTask;
        }
    }

    private sealed class StubAuthenticationSession
        : IAuthenticationSession
    {
        public bool HasRecovery { get; set; }

        public byte[] CurrentCredential { get; set; } = [];

        public TaskCompletionSource? PrepareBlocker { get; set; }

        public TaskCompletionSource PrepareEntered { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<bool> HasPendingRecoveryAsync(
            CancellationToken cancellationToken) =>
            Task.FromResult(HasRecovery);

        public Task PrepareForLoginAsync(
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public async Task PrepareForProfileAsync(
            ReadOnlyMemory<byte> credential,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            PrepareEntered.TrySetResult();
            if (PrepareBlocker is not null)
            {
                await PrepareBlocker.Task.WaitAsync(cancellationToken);
            }

            HasRecovery = true;
            CurrentCredential = credential.ToArray();
        }

        public Task<byte[]?> ReadCurrentCredentialAsync(
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult<byte[]?>(
                CurrentCredential.ToArray());
        }

        public Task RestorePreviousAsync(
            CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task ClearRecoveryAsync(
            CancellationToken cancellationToken)
        {
            HasRecovery = false;
            return Task.CompletedTask;
        }
    }

    private sealed class StubCodexController : ICodexLoginController
    {
        public bool IsRunning { get; set; }

        public Task<bool> IsRunningAsync(
            CancellationToken cancellationToken) =>
            Task.FromResult(IsRunning);

        public Task<CodexStopStatus> RequestStopAsync(
            CancellationToken cancellationToken) =>
            Task.FromResult(CodexStopStatus.Stopped);

        public Task<bool> ForceStopAsync(
            CancellationToken cancellationToken) =>
            Task.FromResult(true);

        public Task<CodexLaunchStatus> LaunchAsync(
            CancellationToken cancellationToken)
        {
            IsRunning = true;
            return Task.FromResult(CodexLaunchStatus.Launched);
        }

        public Task<bool> WaitForRunningAsync(
            CancellationToken cancellationToken) =>
            Task.FromResult(true);
    }
}
