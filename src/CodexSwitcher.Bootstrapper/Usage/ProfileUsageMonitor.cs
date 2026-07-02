using CodexSwitcher.Core.Profiles;
using CodexSwitcher.Core.Usage;

namespace CodexSwitcher.Bootstrapper.Usage;

public sealed class ProfileUsageMonitor : IDisposable
{
    private readonly ListProfilesUseCase _listProfiles;
    private readonly GetProfileRuntimeStateUseCase _getRuntimeState;
    private readonly RefreshProfileRateLimitUseCase _refreshProfile;
    private readonly IProfileRateLimitReader _reader;
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private readonly object _stateGate = new();
    private readonly Dictionary<ProfileId, ProfileRateLimitSnapshot>
        _snapshots = [];
    private readonly Dictionary<ProfileId, ProfileRateLimitSnapshot>
        _publishedSnapshots = [];
    private readonly Dictionary<ProfileId, DateTimeOffset>
        _lastAttempts = [];
    private readonly Dictionary<ProfileId, int> _consecutiveFailures = [];
    private CancellationTokenSource? _monitorCancellation;
    private int _visibleSurfaceCount;
    private int _pauseCount;
    private bool _disposed;

    public ProfileUsageMonitor(
        ListProfilesUseCase listProfiles,
        GetProfileRuntimeStateUseCase getRuntimeState,
        RefreshProfileRateLimitUseCase refreshProfile,
        IProfileRateLimitReader reader)
    {
        _listProfiles = listProfiles;
        _getRuntimeState = getRuntimeState;
        _refreshProfile = refreshProfile;
        _reader = reader;
    }

    public event EventHandler<ProfileRateLimitSnapshot>? SnapshotChanged;

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
            forceAll: true,
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
                await RefreshCycleAsync(
                    forceAll: true,
                    cancellationToken);
            }
            catch (Exception)
                when (!cancellationToken.IsCancellationRequested)
            {
                UsageMonitorDiagnostics.Write(
                    "initial refresh cycle failed");
            }

            using var timer = new PeriodicTimer(
                ProfileUsageRefreshPolicy.ActiveRefreshInterval);
            while (await timer.WaitForNextTickAsync(
                       cancellationToken))
            {
                try
                {
                    await RefreshCycleAsync(
                        forceAll: false,
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

    private async Task RefreshCycleAsync(
        bool forceAll,
        CancellationToken cancellationToken)
    {
        if (!ShouldRun() ||
            !await _refreshGate.WaitAsync(
                millisecondsTimeout: 0,
                cancellationToken))
        {
            UsageMonitorDiagnostics.Write(
                $"refresh cycle skipped forceAll={forceAll}");
            return;
        }

        UsageMonitorDiagnostics.Write(
            $"refresh cycle started forceAll={forceAll}");
        if (forceAll)
        {
            RefreshingChanged?.Invoke(this, true);
        }
        try
        {
            var profiles = await _listProfiles.ExecuteAsync(
                cancellationToken);
            var runtime = await _getRuntimeState.ExecuteAsync(
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

            RemoveDeletedProfiles(profiles.Profiles);
            var now = DateTimeOffset.Now;
            var orderedProfiles = profiles.Profiles
                .OrderByDescending(
                    profile =>
                        activeProfileId is not null &&
                        profile.Id == activeProfileId)
                .ThenBy(profile => profile.Name.Value)
                .ToArray();

            foreach (var profile in orderedProfiles)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!ShouldRun())
                {
                    return;
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

                if (!forceAll &&
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
                if (forceAll)
                {
                    SetLoading(profile.Id);
                }
                ProfileRateLimitSnapshot refreshed;
                try
                {
                    UsageMonitorDiagnostics.Write(
                        $"profile refresh started active={isActive}");
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

                ApplyRefreshResult(
                    refreshed,
                    isActive);
            }

            PublishAll();
        }
        finally
        {
            if (forceAll)
            {
                RefreshingChanged?.Invoke(this, false);
            }
            UsageMonitorDiagnostics.Write(
                $"refresh cycle finished forceAll={forceAll}");
            _refreshGate.Release();
        }
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

    private void ApplyRefreshResult(
        ProfileRateLimitSnapshot refreshed,
        bool isActive)
    {
        _snapshots.TryGetValue(
            refreshed.ProfileId,
            out var previous);

        if (refreshed.Status ==
            ProfileRateLimitStatus.Available)
        {
            _consecutiveFailures[refreshed.ProfileId] = 0;
            _snapshots[refreshed.ProfileId] = refreshed;
        }
        else
        {
            if (isActive)
            {
                _consecutiveFailures[refreshed.ProfileId] =
                    _consecutiveFailures.GetValueOrDefault(
                        refreshed.ProfileId) + 1;
            }

            _snapshots[refreshed.ProfileId] =
                new ProfileRateLimitSnapshot(
                    refreshed.ProfileId,
                    refreshed.Status,
                    previous?.FiveHourLimit,
                    previous?.WeeklyLimit,
                    previous?.LastSuccessfulAt);
        }

        if (PublishIfChanged(_snapshots[refreshed.ProfileId]))
        {
            UsageMonitorDiagnostics.Write(
                $"snapshot published status={_snapshots[refreshed.ProfileId].Status}");
        }
    }

    private void RemoveDeletedProfiles(
        IReadOnlyList<Profile> profiles)
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
            _consecutiveFailures.Remove(profileId);
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
