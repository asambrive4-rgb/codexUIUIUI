using CodexSwitcher.Core.Profiles;
using CodexSwitcher.Core.Usage;

namespace CodexSwitcher.Bootstrapper.Usage;

public sealed class ProfileUsageMonitor : IDisposable
{
    /// <summary>
    /// 런타임·사용량을 한 루프에서 돌리는 간격.
    /// 사용량 probe 자체는 <see cref="ProfileUsageRefreshPolicy"/>와 캐시 TTL로 더 드물게 수행한다.
    /// </summary>
    public static readonly TimeSpan MonitorInterval =
        TimeSpan.FromSeconds(3);

    private readonly ListProfilesUseCase _listProfiles;
    private readonly GetProfileRuntimeStateUseCase _getRuntimeState;
    private readonly RefreshProfileRateLimitUseCase _refreshProfile;
    private readonly IProfileRateLimitReader _reader;
    private readonly IProfileUsageSnapshotCache _snapshotCache;
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private readonly object _stateGate = new();
    private readonly Dictionary<ProfileId, ProfileRateLimitSnapshot>
        _snapshots = [];
    private readonly Dictionary<ProfileId, ProfileRateLimitSnapshot>
        _publishedSnapshots = [];
    private readonly Dictionary<ProfileId, DateTimeOffset>
        _lastAttempts = [];
    private readonly Dictionary<ProfileId, DateTimeOffset>
        _cacheCapturedAt = [];
    private readonly Dictionary<ProfileId, int> _consecutiveFailures = [];
    private CancellationTokenSource? _monitorCancellation;
    private ProfileRuntimeState? _publishedRuntimeState;
    private int _visibleSurfaceCount;
    private int _pauseCount;
    private bool _disposed;
    private bool _cacheLoadStarted;

    public ProfileUsageMonitor(
        ListProfilesUseCase listProfiles,
        GetProfileRuntimeStateUseCase getRuntimeState,
        RefreshProfileRateLimitUseCase refreshProfile,
        IProfileRateLimitReader reader,
        IProfileUsageSnapshotCache snapshotCache)
    {
        _listProfiles = listProfiles;
        _getRuntimeState = getRuntimeState;
        _refreshProfile = refreshProfile;
        _reader = reader;
        _snapshotCache = snapshotCache;
    }

    public event EventHandler<ProfileRateLimitSnapshot>? SnapshotChanged;

    public event EventHandler<ProfileRuntimeState>? RuntimeStateChanged;

    public event EventHandler<bool>? RefreshingChanged;

    public IDisposable AcquireVisibleSurface()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        lock (_stateGate)
        {
            _visibleSurfaceCount++;
            if (_visibleSurfaceCount == 1)
            {
                UsageMonitorDiagnostics.Write(
                    "visible surface acquired; starting monitor");
                StartMonitor();
            }
        }

        return new Lease(ReleaseVisibleSurface);
    }

    public async Task<IDisposable> PauseAsync(
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        lock (_stateGate)
        {
            _pauseCount++;
        }

        try
        {
            await _refreshGate.WaitAsync(cancellationToken);
            _refreshGate.Release();
            return new Lease(Resume);
        }
        catch
        {
            Resume();
            throw;
        }
    }

    public Task RefreshAllNowAsync(
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return RefreshCycleAsync(
            UsageRefreshKind.ManualForce,
            forceRuntimeRefresh: true,
            cancellationToken);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        CancellationTokenSource? cancellation;
        lock (_stateGate)
        {
            cancellation = _monitorCancellation;
            _monitorCancellation = null;
            _visibleSurfaceCount = 0;
        }

        cancellation?.Cancel();
        cancellation?.Dispose();
        _reader.Dispose();
    }

    private void StartMonitor()
    {
        _monitorCancellation?.Cancel();
        _monitorCancellation?.Dispose();
        _monitorCancellation = new CancellationTokenSource();
        UsageMonitorDiagnostics.Write("monitor task scheduled");
        _ = MonitorAsync(_monitorCancellation.Token);
    }

    private void ReleaseVisibleSurface()
    {
        CancellationTokenSource? cancellation = null;
        lock (_stateGate)
        {
            if (_visibleSurfaceCount == 0)
            {
                return;
            }

            _visibleSurfaceCount--;
            if (_visibleSurfaceCount == 0)
            {
                cancellation = _monitorCancellation;
                _monitorCancellation = null;
            }
        }

        cancellation?.Cancel();
        cancellation?.Dispose();
        if (cancellation is not null)
        {
            UsageMonitorDiagnostics.Write(
                "visible surface released; stopping monitor");
            _ = Task.Run(_reader.StopActiveSession);
        }
    }

    private void Resume()
    {
        var shouldRefresh = false;
        lock (_stateGate)
        {
            if (_pauseCount > 0)
            {
                _pauseCount--;
            }

            shouldRefresh =
                _pauseCount == 0 &&
                _visibleSurfaceCount > 0;
        }

        if (shouldRefresh)
        {
            // 작업 직후에는 캐시를 무시하고 최신을 맞춘다.
            _ = RefreshAllNowAsync(CancellationToken.None);
        }
    }

    private async Task MonitorAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            UsageMonitorDiagnostics.Write("monitor started");
            try
            {
                await EnsureCacheLoadedAsync(cancellationToken);
                await RefreshCycleAsync(
                    UsageRefreshKind.OpenSurface,
                    forceRuntimeRefresh: true,
                    cancellationToken);
            }
            catch (Exception)
                when (!cancellationToken.IsCancellationRequested)
            {
                UsageMonitorDiagnostics.Write(
                    "initial refresh cycle failed");
            }

            using var timer = new PeriodicTimer(MonitorInterval);
            while (await timer.WaitForNextTickAsync(
                       cancellationToken))
            {
                try
                {
                    await RefreshCycleAsync(
                        UsageRefreshKind.Scheduled,
                        forceRuntimeRefresh: false,
                        cancellationToken);
                }
                catch (Exception)
                    when (!cancellationToken.IsCancellationRequested)
                {
                    UsageMonitorDiagnostics.Write(
                        "refresh cycle failed");
                }
            }
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            // 마지막 사용량 화면이 닫히면 감시를 끝낸다.
        }
    }

    private async Task EnsureCacheLoadedAsync(
        CancellationToken cancellationToken)
    {
        if (_cacheLoadStarted)
        {
            return;
        }

        _cacheLoadStarted = true;
        try
        {
            await _snapshotCache.LoadAsync(cancellationToken);
            UsageMonitorDiagnostics.Write("usage snapshot cache loaded");
        }
        catch (Exception)
            when (!cancellationToken.IsCancellationRequested)
        {
            UsageMonitorDiagnostics.Write(
                "usage snapshot cache load failed");
        }
    }

    private async Task RefreshCycleAsync(
        UsageRefreshKind kind,
        bool forceRuntimeRefresh,
        CancellationToken cancellationToken)
    {
        if (!ShouldRun() ||
            !await _refreshGate.WaitAsync(
                millisecondsTimeout: 0,
                cancellationToken))
        {
            UsageMonitorDiagnostics.Write(
                $"refresh cycle skipped kind={kind}");
            return;
        }

        var ignoreCache =
            kind == UsageRefreshKind.ManualForce;
        var showRefreshing =
            kind is UsageRefreshKind.ManualForce
                or UsageRefreshKind.OpenSurface;

        UsageMonitorDiagnostics.Write(
            $"refresh cycle started kind={kind} forceRuntime={forceRuntimeRefresh}");
        if (showRefreshing)
        {
            RefreshingChanged?.Invoke(this, true);
        }

        try
        {
            await EnsureCacheLoadedAsync(cancellationToken);

            // 한 사이클에 runtime을 한 번만 조회한다.
            var runtime = await _getRuntimeState.ExecuteAsync(
                forceRuntimeRefresh,
                cancellationToken);
            PublishRuntime(runtime);

            if (!ShouldRun())
            {
                return;
            }

            var profiles = await _listProfiles.ExecuteAsync(
                cancellationToken);
            UsageMonitorDiagnostics.Write(
                $"profiles={profiles.Profiles.Count} runtime={runtime.Status}");
            var activeProfileId =
                runtime.Status ==
                ProfileRuntimeStatus.RunningKnownProfile
                    ? runtime.ActiveProfileId
                    : null;

            if (runtime.Status ==
                ProfileRuntimeStatus.RunningUnknownProfile)
            {
                UsageMonitorDiagnostics.Write(
                    "runtime unknown; probing stored profiles as inactive");
                _reader.StopActiveSession();
            }

            if (activeProfileId is null)
            {
                UsageMonitorDiagnostics.Write(
                    "no active profile; stopping active usage session");
                _reader.StopActiveSession();
            }

            await RemoveDeletedProfilesAsync(
                profiles.Profiles,
                cancellationToken);

            // 캐시 hit를 먼저 UI에 올려 표면 재오픈 체감을 줄인다.
            HydrateFromCache(profiles.Profiles);
            PublishAll();

            var now = DateTimeOffset.Now;
            var orderedProfiles = profiles.Profiles
                .OrderByDescending(
                    profile =>
                        activeProfileId is not null &&
                        profile.Id == activeProfileId)
                .ThenBy(profile => profile.Name.Value)
                .ToArray();

            var probeBudget = kind == UsageRefreshKind.ManualForce
                ? int.MaxValue
                : ProfileUsageRefreshPolicy.ScheduledProbeBudget;
            var probesRemaining = probeBudget;

            foreach (var profile in orderedProfiles)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!ShouldRun())
                {
                    break;
                }

                if (probesRemaining <= 0)
                {
                    UsageMonitorDiagnostics.Write(
                        "probe budget exhausted; deferring remaining profiles");
                    break;
                }

                var isActive =
                    activeProfileId is not null &&
                    profile.Id == activeProfileId;
                _lastAttempts.TryGetValue(
                    profile.Id,
                    out var lastAttempt);
                _consecutiveFailures.TryGetValue(
                    profile.Id,
                    out var failureCount);

                if (!ignoreCache &&
                    IsFreshFromCache(
                        profile.Id,
                        isActive,
                        now))
                {
                    Publish(profile.Id);
                    continue;
                }

                if (!ignoreCache &&
                    !ProfileUsageRefreshPolicy.IsDue(
                        now,
                        lastAttempt == default
                            ? null
                            : lastAttempt,
                        isActive,
                        failureCount))
                {
                    Publish(profile.Id);
                    continue;
                }

                _lastAttempts[profile.Id] = now;
                var hasDisplayData =
                    _snapshots.TryGetValue(profile.Id, out var existing) &&
                    (existing.FiveHourLimit is not null ||
                     existing.WeeklyLimit is not null ||
                     existing.Status ==
                     ProfileRateLimitStatus.Available);
                // 캐시 miss이거나 수동 강제일 때만 Loading으로 깜빡인다.
                if (ignoreCache || !hasDisplayData)
                {
                    SetLoading(profile.Id);
                }

                ProfileRateLimitSnapshot refreshed;
                try
                {
                    UsageMonitorDiagnostics.Write(
                        $"profile refresh started active={isActive} kind={kind}");
                    refreshed =
                        await _refreshProfile.ExecuteAsync(
                            profile.Id,
                            keepAlive: isActive,
                            cancellationToken);
                    UsageMonitorDiagnostics.Write(
                        $"profile refresh completed status={refreshed.Status}");
                }
                catch (OperationCanceledException)
                    when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch
                {
                    UsageMonitorDiagnostics.Write(
                        "profile refresh threw; publishing failed snapshot");
                    refreshed = new ProfileRateLimitSnapshot(
                        profile.Id,
                        ProfileRateLimitStatus.Failed);
                }

                await ApplyRefreshResultAsync(
                    refreshed,
                    isActive,
                    now,
                    cancellationToken);
                probesRemaining--;
            }

            PublishAll();
        }
        finally
        {
            if (showRefreshing)
            {
                RefreshingChanged?.Invoke(this, false);
            }

            UsageMonitorDiagnostics.Write(
                $"refresh cycle finished kind={kind}");
            _refreshGate.Release();
        }
    }

    private void HydrateFromCache(IReadOnlyList<Profile> profiles)
    {
        foreach (var profile in profiles)
        {
            if (!_snapshotCache.TryGet(profile.Id, out var entry))
            {
                continue;
            }

            if (entry.Status == ProfileRateLimitStatus.Loading)
            {
                continue;
            }

            _snapshots[profile.Id] = entry.ToSnapshot();
            _cacheCapturedAt[profile.Id] = entry.CapturedAt;
            if (!_lastAttempts.ContainsKey(profile.Id))
            {
                _lastAttempts[profile.Id] = entry.CapturedAt;
            }
        }
    }

    private bool IsFreshFromCache(
        ProfileId profileId,
        bool isActive,
        DateTimeOffset now)
    {
        if (_snapshotCache.TryGet(profileId, out var entry))
        {
            return ProfileUsageRefreshPolicy.IsCacheFresh(
                now,
                entry.CapturedAt,
                entry.Status,
                isActive);
        }

        if (_snapshots.TryGetValue(profileId, out var snapshot) &&
            _cacheCapturedAt.TryGetValue(profileId, out var capturedAt))
        {
            return ProfileUsageRefreshPolicy.IsCacheFresh(
                now,
                capturedAt,
                snapshot.Status,
                isActive);
        }

        return false;
    }

    private bool ShouldRun()
    {
        lock (_stateGate)
        {
            return !_disposed &&
                   _visibleSurfaceCount > 0 &&
                   _pauseCount == 0;
        }
    }

    private void PublishRuntime(ProfileRuntimeState runtime)
    {
        // 복구 플래그 등 UI 부가 상태를 주기적으로 맞추기 위해
        // 값이 같아도 사이클마다 한 번씩 알린다. VM 쪽에서 표시 스킵한다.
        var changed = _publishedRuntimeState is null ||
                      !EqualityComparer<ProfileRuntimeState>.Default.Equals(
                          _publishedRuntimeState,
                          runtime);
        _publishedRuntimeState = runtime;
        if (changed)
        {
            UsageMonitorDiagnostics.Write(
                $"runtime published status={runtime.Status}");
        }

        RuntimeStateChanged?.Invoke(this, runtime);
    }

    private void SetLoading(ProfileId profileId)
    {
        _snapshots.TryGetValue(
            profileId,
            out var previous);
        var loading = new ProfileRateLimitSnapshot(
            profileId,
            ProfileRateLimitStatus.Loading,
            previous?.FiveHourLimit,
            previous?.WeeklyLimit,
            previous?.LastSuccessfulAt);
        _snapshots[profileId] = loading;
        if (PublishIfChanged(loading))
        {
            UsageMonitorDiagnostics.Write("loading snapshot published");
        }
    }

    private async Task ApplyRefreshResultAsync(
        ProfileRateLimitSnapshot refreshed,
        bool isActive,
        DateTimeOffset capturedAt,
        CancellationToken cancellationToken)
    {
        _snapshots.TryGetValue(
            refreshed.ProfileId,
            out var previous);

        ProfileRateLimitSnapshot stored;
        if (refreshed.Status ==
            ProfileRateLimitStatus.Available)
        {
            _consecutiveFailures[refreshed.ProfileId] = 0;
            stored = refreshed;
        }
        else
        {
            if (isActive)
            {
                _consecutiveFailures[refreshed.ProfileId] =
                    _consecutiveFailures.GetValueOrDefault(
                        refreshed.ProfileId) + 1;
            }

            stored = new ProfileRateLimitSnapshot(
                refreshed.ProfileId,
                refreshed.Status,
                previous?.FiveHourLimit,
                previous?.WeeklyLimit,
                previous?.LastSuccessfulAt);
        }

        _snapshots[refreshed.ProfileId] = stored;
        _cacheCapturedAt[refreshed.ProfileId] = capturedAt;

        if (stored.Status != ProfileRateLimitStatus.Loading)
        {
            try
            {
                await _snapshotCache.SetAsync(
                    CachedProfileUsageEntry.FromSnapshot(
                        stored,
                        capturedAt),
                    cancellationToken);
            }
            catch (Exception)
                when (!cancellationToken.IsCancellationRequested)
            {
                UsageMonitorDiagnostics.Write(
                    "usage snapshot cache set failed");
            }
        }

        if (PublishIfChanged(stored))
        {
            UsageMonitorDiagnostics.Write(
                $"snapshot published status={stored.Status}");
        }
    }

    private async Task RemoveDeletedProfilesAsync(
        IReadOnlyList<Profile> profiles,
        CancellationToken cancellationToken)
    {
        var existing = profiles
            .Select(profile => profile.Id)
            .ToHashSet();
        foreach (var profileId in _snapshots.Keys
                     .Where(id => !existing.Contains(id))
                     .ToArray())
        {
            _snapshots.Remove(profileId);
            _publishedSnapshots.Remove(profileId);
            _lastAttempts.Remove(profileId);
            _cacheCapturedAt.Remove(profileId);
            _consecutiveFailures.Remove(profileId);
        }

        try
        {
            await _snapshotCache.RemoveMissingAsync(
                existing,
                cancellationToken);
        }
        catch (Exception)
            when (!cancellationToken.IsCancellationRequested)
        {
            UsageMonitorDiagnostics.Write(
                "usage snapshot cache remove-missing failed");
        }
    }

    private void Publish(ProfileId profileId)
    {
        if (_snapshots.TryGetValue(
                profileId,
                out var snapshot))
        {
            PublishIfChanged(snapshot);
        }
    }

    private void PublishAll()
    {
        foreach (var snapshot in _snapshots.Values)
        {
            PublishIfChanged(snapshot);
        }
    }

    private bool PublishIfChanged(ProfileRateLimitSnapshot snapshot)
    {
        if (_publishedSnapshots.TryGetValue(
                snapshot.ProfileId,
                out var previous) &&
            HasSamePublishedSurface(previous, snapshot))
        {
            return false;
        }

        _publishedSnapshots[snapshot.ProfileId] = snapshot;
        SnapshotChanged?.Invoke(this, snapshot);
        return true;
    }

    internal static bool HasSamePublishedSurface(
        ProfileRateLimitSnapshot previous,
        ProfileRateLimitSnapshot current) =>
        previous.ProfileId == current.ProfileId &&
        previous.Status == current.Status &&
        previous.FiveHourLimit == current.FiveHourLimit &&
        previous.WeeklyLimit == current.WeeklyLimit;

    private enum UsageRefreshKind
    {
        /// <summary>주기 틱 — 캐시·due 존중, probe 예산 1.</summary>
        Scheduled,

        /// <summary>표면 최초 표시 — 캐시 선발행 후 stale만 probe.</summary>
        OpenSurface,

        /// <summary>수동 새로고침/작업 후 — 캐시 무시, 전 프로필.</summary>
        ManualForce
    }

    private sealed class Lease : IDisposable
    {
        private Action? _release;

        public Lease(Action release)
        {
            _release = release;
        }

        public void Dispose()
        {
            Interlocked.Exchange(ref _release, null)?.Invoke();
        }
    }
}
