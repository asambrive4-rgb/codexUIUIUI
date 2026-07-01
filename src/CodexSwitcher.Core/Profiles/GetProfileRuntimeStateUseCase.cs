using System.Security.Cryptography;

namespace CodexSwitcher.Core.Profiles;

public sealed class GetProfileRuntimeStateUseCase
{
    private static readonly TimeSpan RunningStateCacheDuration =
        TimeSpan.FromSeconds(5);

    private readonly IProfileStore _profileStore;
    private readonly IAuthenticationSession _authenticationSession;
    private readonly ICodexLoginController _codexController;
    private readonly ICredentialIdentityReader _identityReader;
    private readonly TimeProvider _timeProvider;
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private readonly object _cacheGate = new();
    private CachedRuntimeState? _cachedState;

    public GetProfileRuntimeStateUseCase(
        IProfileStore profileStore,
        IAuthenticationSession authenticationSession,
        ICodexLoginController codexController)
        : this(
            profileStore,
            authenticationSession,
            codexController,
            new NoCredentialIdentityReader())
    {
    }

    public GetProfileRuntimeStateUseCase(
        IProfileStore profileStore,
        IAuthenticationSession authenticationSession,
        ICodexLoginController codexController,
        ICredentialIdentityReader identityReader)
        : this(
            profileStore,
            authenticationSession,
            codexController,
            identityReader,
            TimeProvider.System)
    {
    }

    public GetProfileRuntimeStateUseCase(
        IProfileStore profileStore,
        IAuthenticationSession authenticationSession,
        ICodexLoginController codexController,
        ICredentialIdentityReader identityReader,
        TimeProvider timeProvider)
    {
        _profileStore = profileStore;
        _authenticationSession = authenticationSession;
        _codexController = codexController;
        _identityReader = identityReader;
        _timeProvider = timeProvider;
    }

    public Task<ProfileRuntimeState> ExecuteAsync(
        CancellationToken cancellationToken) =>
        ExecuteAsync(
            forceRefresh: false,
            cancellationToken);

    public async Task<ProfileRuntimeState> ExecuteAsync(
        bool forceRefresh,
        CancellationToken cancellationToken)
    {
        if (!await _codexController.IsRunningAsync(cancellationToken))
        {
            var stopped = new ProfileRuntimeState(
                ProfileRuntimeStatus.Stopped);
            Cache(stopped);
            return stopped;
        }

        var cached = GetFreshCachedRunningState(forceRefresh);
        if (cached is not null)
        {
            return cached;
        }

        await _refreshGate.WaitAsync(cancellationToken);
        try
        {
            cached = GetFreshCachedRunningState(forceRefresh);
            if (cached is not null)
            {
                return cached;
            }

            var refreshed = await ResolveRunningStateAsync(
                cancellationToken);
            Cache(refreshed);
            return refreshed;
        }
        finally
        {
            _refreshGate.Release();
        }
    }

    private async Task<ProfileRuntimeState> ResolveRunningStateAsync(
        CancellationToken cancellationToken)
    {
        byte[]? currentCredential = null;
        try
        {
            currentCredential =
                await _authenticationSession.ReadCurrentCredentialAsync(
                    cancellationToken);
            if (currentCredential is null)
            {
                return Unknown();
            }

            var stored = await _profileStore.ReadAllAsync(
                cancellationToken);
            var matches = new List<ProfileId>();
            var currentAccountId =
                _identityReader.TryReadAccountId(currentCredential);

            foreach (var profile in stored.Profiles)
            {
                byte[]? storedCredential = null;
                try
                {
                    storedCredential =
                        await _profileStore.ReadCredentialAsync(
                            profile.Id,
                            cancellationToken);
                    if (storedCredential.Length ==
                        currentCredential.Length &&
                        CryptographicOperations.FixedTimeEquals(
                            storedCredential,
                            currentCredential))
                    {
                        matches.Add(profile.Id);
                        continue;
                    }

                    if (currentAccountId is not null &&
                        StringComparer.Ordinal.Equals(
                            currentAccountId,
                            _identityReader.TryReadAccountId(
                                storedCredential)))
                    {
                        matches.Add(profile.Id);
                    }
                }
                catch (Exception exception)
                    when (exception is IOException or
                          UnauthorizedAccessException or
                          InvalidDataException or
                          CryptographicException)
                {
                    // 읽을 수 없는 프로필은 활성 후보에서 제외한다.
                }
                finally
                {
                    if (storedCredential is not null)
                    {
                        CryptographicOperations.ZeroMemory(
                            storedCredential);
                    }
                }
            }

            return matches.Count == 1
                ? new ProfileRuntimeState(
                    ProfileRuntimeStatus.RunningKnownProfile,
                    matches[0])
                : Unknown();
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return Unknown();
        }
        finally
        {
            if (currentCredential is not null)
            {
                CryptographicOperations.ZeroMemory(
                    currentCredential);
            }
        }
    }

    private ProfileRuntimeState? GetFreshCachedRunningState(
        bool forceRefresh)
    {
        if (forceRefresh)
        {
            return null;
        }

        var now = _timeProvider.GetUtcNow();
        lock (_cacheGate)
        {
            if (_cachedState is null ||
                _cachedState.State.Status ==
                ProfileRuntimeStatus.Stopped ||
                now - _cachedState.CapturedAt >
                RunningStateCacheDuration)
            {
                return null;
            }

            return _cachedState.State;
        }
    }

    private void Cache(ProfileRuntimeState state)
    {
        lock (_cacheGate)
        {
            _cachedState = new CachedRuntimeState(
                state,
                _timeProvider.GetUtcNow());
        }
    }

    private static ProfileRuntimeState Unknown() =>
        new(ProfileRuntimeStatus.RunningUnknownProfile);

    private sealed record CachedRuntimeState(
        ProfileRuntimeState State,
        DateTimeOffset CapturedAt);

    private sealed class NoCredentialIdentityReader
        : ICredentialIdentityReader
    {
        public string? TryReadAccountId(
            ReadOnlySpan<byte> credential) => null;
    }
}
