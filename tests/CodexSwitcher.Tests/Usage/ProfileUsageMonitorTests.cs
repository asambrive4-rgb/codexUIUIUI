using CodexSwitcher.Bootstrapper.Usage;
using CodexSwitcher.Core.Profiles;
using CodexSwitcher.Core.Usage;
using CodexSwitcher.Infrastructure.Usage;

namespace CodexSwitcher.Tests.Usage;

[TestClass]
public sealed class ProfileUsageMonitorTests
{
    [TestMethod]
    public async Task Monitor_WhenRuntimeUnknown_ProbesStoredProfilesWithinBudget()
    {
        var store = new StubProfileStore(
            new Profile(
                ProfileId.New(),
                ProfileName.Create("A")),
            new Profile(
                ProfileId.New(),
                ProfileName.Create("B")));
        var reader = new RecordingRateLimitReader();
        using var monitor = CreateMonitor(store, reader);

        using var surface = monitor.AcquireVisibleSurface();
        // OpenSurface 예산 1 + 이후 Scheduled 틱(3초)으로 나머지 probe
        await WaitUntilAsync(
            () => reader.CallCount >= 2,
            TimeSpan.FromSeconds(8));

        Assert.IsGreaterThanOrEqualTo(2, reader.CallCount);
        Assert.IsTrue(
            reader.KeepAliveValues.All(keepAlive => !keepAlive));
    }

    [TestMethod]
    public async Task Monitor_OpenSurface_ProbesAtMostOneProfileWhenCacheEmpty()
    {
        var store = new StubProfileStore(
            new Profile(
                ProfileId.New(),
                ProfileName.Create("A")),
            new Profile(
                ProfileId.New(),
                ProfileName.Create("B")));
        var reader = new RecordingRateLimitReader();
        using var monitor = CreateMonitor(store, reader);

        using var surface = monitor.AcquireVisibleSurface();
        await WaitUntilAsync(
            () => reader.CallCount >= 1,
            TimeSpan.FromSeconds(2));
        await Task.Delay(100);

        Assert.AreEqual(1, reader.CallCount);
    }

    [TestMethod]
    public async Task Monitor_InitialRefresh_DoesNotRepublishUnchangedSnapshot()
    {
        var store = new StubProfileStore(
            new Profile(
                ProfileId.New(),
                ProfileName.Create("A")));
        var reader = new RecordingRateLimitReader();
        using var monitor = CreateMonitor(store, reader);
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

    [TestMethod]
    public async Task Monitor_PublishesRuntimeState_OnVisibleSurface()
    {
        var store = new StubProfileStore(
            new Profile(
                ProfileId.New(),
                ProfileName.Create("A")));
        var reader = new RecordingRateLimitReader();
        using var monitor = CreateMonitor(store, reader);
        ProfileRuntimeState? published = null;
        monitor.RuntimeStateChanged += (_, state) =>
            published = state;

        using var surface = monitor.AcquireVisibleSurface();
        await WaitUntilAsync(
            () => published is not null,
            TimeSpan.FromSeconds(2));

        Assert.IsNotNull(published);
        Assert.AreEqual(
            ProfileRuntimeStatus.RunningUnknownProfile,
            published.Status);
    }

    [TestMethod]
    public async Task Monitor_WhenCacheFresh_SkipsReader()
    {
        var profile = new Profile(
            ProfileId.New(),
            ProfileName.Create("Cached"));
        var store = new StubProfileStore(profile);
        var cache = new MemoryProfileUsageSnapshotCache();
        await cache.SetAsync(
            new CachedProfileUsageEntry(
                profile.Id,
                ProfileRateLimitStatus.Available,
                new RateLimitWindow(10, 300, null),
                new RateLimitWindow(20, 10_080, null),
                DateTimeOffset.Now,
                DateTimeOffset.Now),
            CancellationToken.None);
        var reader = new RecordingRateLimitReader();
        using var monitor = CreateMonitor(store, reader, cache);
        ProfileRateLimitSnapshot? published = null;
        monitor.SnapshotChanged += (_, snapshot) =>
            published = snapshot;

        using var surface = monitor.AcquireVisibleSurface();
        await WaitUntilAsync(
            () => published is not null &&
                  published.Status ==
                  ProfileRateLimitStatus.Available,
            TimeSpan.FromSeconds(2));
        await Task.Delay(100);

        Assert.AreEqual(0, reader.CallCount);
        Assert.AreEqual(
            ProfileRateLimitStatus.Available,
            published!.Status);
    }

    [TestMethod]
    public async Task Monitor_ManualForce_IgnoresFreshCache()
    {
        var profile = new Profile(
            ProfileId.New(),
            ProfileName.Create("Cached"));
        var store = new StubProfileStore(profile);
        var cache = new MemoryProfileUsageSnapshotCache();
        await cache.SetAsync(
            new CachedProfileUsageEntry(
                profile.Id,
                ProfileRateLimitStatus.Available,
                new RateLimitWindow(10, 300, null),
                null,
                DateTimeOffset.Now,
                DateTimeOffset.Now),
            CancellationToken.None);
        var reader = new RecordingRateLimitReader();
        using var monitor = CreateMonitor(store, reader, cache);

        using var surface = monitor.AcquireVisibleSurface();
        await WaitUntilAsync(
            () => true,
            TimeSpan.FromMilliseconds(200));
        // 캐시 hit로 OpenSurface는 reader를 안 부를 수 있다.
        await monitor.RefreshAllNowAsync(CancellationToken.None);
        await WaitUntilAsync(
            () => reader.CallCount >= 1,
            TimeSpan.FromSeconds(2));

        Assert.IsGreaterThanOrEqualTo(1, reader.CallCount);
    }

    [TestMethod]
    public void HasSamePublishedSurface_IgnoresLastSuccessfulAtOnly()
    {
        var profileId = ProfileId.New();
        var fiveHour = new RateLimitWindow(
            10,
            300,
            DateTimeOffset.UtcNow.AddHours(1));
        var weekly = new RateLimitWindow(
            40,
            10_080,
            DateTimeOffset.UtcNow.AddDays(2));
        var previous = new ProfileRateLimitSnapshot(
            profileId,
            ProfileRateLimitStatus.Available,
            fiveHour,
            weekly,
            DateTimeOffset.UtcNow.AddMinutes(-10));
        var current = previous with
        {
            LastSuccessfulAt = DateTimeOffset.UtcNow
        };

        Assert.IsTrue(
            ProfileUsageMonitor.HasSamePublishedSurface(
                previous,
                current));
    }

    private static ProfileUsageMonitor CreateMonitor(
        IProfileStore store,
        IProfileRateLimitReader reader,
        IProfileUsageSnapshotCache? cache = null) =>
        new(
            new ListProfilesUseCase(store),
            new GetProfileRuntimeStateUseCase(
                store,
                new UnknownAuthenticationSession(),
                new RunningCodexController()),
            new RefreshProfileRateLimitUseCase(
                store,
                reader,
                new ProfileOperationCoordinator()),
            reader,
            cache ?? new MemoryProfileUsageSnapshotCache());

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
