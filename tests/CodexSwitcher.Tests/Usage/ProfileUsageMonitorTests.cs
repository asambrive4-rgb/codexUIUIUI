using CodexSwitcher.Bootstrapper.Usage;
using CodexSwitcher.Core.Profiles;
using CodexSwitcher.Core.Usage;

namespace CodexSwitcher.Tests.Usage;

[TestClass]
public sealed class ProfileUsageMonitorTests
{
    [TestMethod]
    public async Task Monitor_WhenRuntimeUnknown_ProbesStoredProfiles()
    {
        var store = new StubProfileStore(
            new Profile(
                ProfileId.New(),
                ProfileName.Create("A")),
            new Profile(
                ProfileId.New(),
                ProfileName.Create("B")));
        var reader = new RecordingRateLimitReader();
        using var monitor = new ProfileUsageMonitor(
            new ListProfilesUseCase(store),
            new GetProfileRuntimeStateUseCase(
                store,
                new UnknownAuthenticationSession(),
                new RunningCodexController()),
            new RefreshProfileRateLimitUseCase(
                store,
                reader,
                new ProfileOperationCoordinator()),
            reader);

        using var surface = monitor.AcquireVisibleSurface();
        await WaitUntilAsync(
            () => reader.CallCount >= 2,
            TimeSpan.FromSeconds(2));

        Assert.HasCount(2, reader.KeepAliveValues);
        CollectionAssert.AreEqual(
            new[] { false, false },
            reader.KeepAliveValues);
    }

    [TestMethod]
    public async Task Monitor_InitialRefresh_DoesNotRepublishUnchangedSnapshot()
    {
        var store = new StubProfileStore(
            new Profile(
                ProfileId.New(),
                ProfileName.Create("A")));
        var reader = new RecordingRateLimitReader();
        using var monitor = new ProfileUsageMonitor(
            new ListProfilesUseCase(store),
            new GetProfileRuntimeStateUseCase(
                store,
                new UnknownAuthenticationSession(),
                new RunningCodexController()),
            new RefreshProfileRateLimitUseCase(
                store,
                reader,
                new ProfileOperationCoordinator()),
            reader);
        var snapshotCount = 0;
        monitor.SnapshotChanged += (_, _) =>
            Interlocked.Increment(ref snapshotCount);

        using var surface = monitor.AcquireVisibleSurface();
        await WaitUntilAsync(
            () => reader.CallCount >= 1,
            TimeSpan.FromSeconds(2));
        await Task.Delay(50);

        Assert.AreEqual(
            2,
            Volatile.Read(ref snapshotCount));
    }

    private static async Task WaitUntilAsync(
        Func<bool> condition,
        TimeSpan timeout)
    {
        var deadline = DateTimeOffset.Now + timeout;
        while (DateTimeOffset.Now < deadline)
        {
            if (condition())
            {
                return;
            }

            await Task.Delay(20);
        }

        Assert.Fail("조건이 시간 안에 만족되지 않았습니다.");
    }

    private sealed class StubProfileStore : IProfileStore
    {
        private readonly IReadOnlyList<Profile> _profiles;

        public StubProfileStore(params Profile[] profiles)
        {
            _profiles = profiles;
        }

        public Task<ProfileStoreReadResult> ReadAllAsync(
            CancellationToken cancellationToken) =>
            Task.FromResult(
                new ProfileStoreReadResult(
                    _profiles,
                    []));

        public Task SaveAsync(
            Profile profile,
            ReadOnlyMemory<byte> credential,
            CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task<byte[]> ReadCredentialAsync(
            ProfileId profileId,
            CancellationToken cancellationToken) =>
            Task.FromResult(
                profileId.Value.ToByteArray());

        public Task ReplaceCredentialAsync(
            ProfileId profileId,
            ReadOnlyMemory<byte> credential,
            CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task DeleteAsync(
            ProfileId profileId,
            CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }

    private sealed class RecordingRateLimitReader
        : IProfileRateLimitReader
    {
        private readonly object _gate = new();

        public int CallCount
        {
            get
            {
                lock (_gate)
                {
                    return KeepAliveValues.Count;
                }
            }
        }

        public List<bool> KeepAliveValues { get; } = [];

        public Task<ProfileRateLimitReadResult> ReadAsync(
            ProfileId profileId,
            ReadOnlyMemory<byte> credential,
            bool keepAlive,
            CancellationToken cancellationToken)
        {
            lock (_gate)
            {
                KeepAliveValues.Add(keepAlive);
            }

            return Task.FromResult(
                new ProfileRateLimitReadResult(
                    ProfileRateLimitStatus.Available,
                    [
                        new RateLimitWindow(
                            10,
                            300,
                            null)
                    ]));
        }

        public void StopActiveSession()
        {
        }

        public void Dispose()
        {
        }
    }

    private sealed class UnknownAuthenticationSession
        : IAuthenticationSession
    {
        public Task<bool> HasPendingRecoveryAsync(
            CancellationToken cancellationToken) =>
            Task.FromResult(false);

        public Task PrepareForLoginAsync(
            CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task PrepareForProfileAsync(
            ReadOnlyMemory<byte> credential,
            CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task<byte[]?> ReadCurrentCredentialAsync(
            CancellationToken cancellationToken) =>
            Task.FromResult<byte[]?>(null);

        public Task RestorePreviousAsync(
            CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task ClearRecoveryAsync(
            CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }

    private sealed class RunningCodexController : ICodexLoginController
    {
        public Task<bool> IsRunningAsync(
            CancellationToken cancellationToken) =>
            Task.FromResult(true);

        public Task<CodexStopStatus> RequestStopAsync(
            CancellationToken cancellationToken) =>
            Task.FromResult(CodexStopStatus.Stopped);

        public Task<bool> ForceStopAsync(
            CancellationToken cancellationToken) =>
            Task.FromResult(true);

        public Task<CodexLaunchStatus> LaunchAsync(
            CancellationToken cancellationToken) =>
            Task.FromResult(CodexLaunchStatus.Launched);

        public Task<bool> WaitForRunningAsync(
            CancellationToken cancellationToken) =>
            Task.FromResult(true);
    }
}
