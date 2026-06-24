using CodexSwitcher.Core.Profiles;
using CodexSwitcher.Core.Usage;

namespace CodexSwitcher.Tests.Usage;

[TestClass]
public sealed class RefreshProfileRateLimitUseCaseTests
{
    [TestMethod]
    public async Task Execute_MapsFiveHourAndWeeklyWindows()
    {
        var profile = CreateProfile();
        var store = new StubProfileStore(profile);
        var reader = new StubRateLimitReader(
            new ProfileRateLimitReadResult(
                ProfileRateLimitStatus.Available,
                [
                    new RateLimitWindow(
                        28,
                        300,
                        DateTimeOffset.UtcNow.AddHours(2)),
                    new RateLimitWindow(
                        59,
                        10_080,
                        DateTimeOffset.UtcNow.AddDays(3))
                ]));
        var useCase = CreateUseCase(store, reader);

        var result = await useCase.ExecuteAsync(
            profile.Id,
            keepAlive: true,
            CancellationToken.None);

        Assert.AreEqual(
            ProfileRateLimitStatus.Available,
            result.Status);
        Assert.AreEqual(
            72,
            result.FiveHourLimit?.RemainingPercent);
        Assert.AreEqual(
            41,
            result.WeeklyLimit?.RemainingPercent);
        Assert.IsNotNull(result.LastSuccessfulAt);
        Assert.IsTrue(reader.LastKeepAlive);
    }

    [TestMethod]
    public async Task Execute_IgnoresUnknownWindowDurations()
    {
        var profile = CreateProfile();
        var store = new StubProfileStore(profile);
        var reader = new StubRateLimitReader(
            new ProfileRateLimitReadResult(
                ProfileRateLimitStatus.Available,
                [
                    new RateLimitWindow(
                        20,
                        60,
                        DateTimeOffset.UtcNow)
                ]));
        var useCase = CreateUseCase(store, reader);

        var result = await useCase.ExecuteAsync(
            profile.Id,
            keepAlive: false,
            CancellationToken.None);

        Assert.IsNull(result.FiveHourLimit);
        Assert.IsNull(result.WeeklyLimit);
    }

    [TestMethod]
    public async Task Execute_WhenCredentialRefreshes_ReplacesStoredCredential()
    {
        var profile = CreateProfile();
        var store = new StubProfileStore(profile);
        var refreshed = "refreshed-credential"u8.ToArray();
        var reader = new StubRateLimitReader(
            new ProfileRateLimitReadResult(
                ProfileRateLimitStatus.Available,
                [],
                refreshed));
        var useCase = CreateUseCase(store, reader);

        _ = await useCase.ExecuteAsync(
            profile.Id,
            keepAlive: false,
            CancellationToken.None);

        CollectionAssert.AreEqual(
            "refreshed-credential"u8.ToArray(),
            store.ReplacedCredential);
        Assert.IsTrue(
            refreshed.All(value => value == 0),
            "조회 결과의 평문 인증은 사용 후 메모리에서 지워야 합니다.");
    }

    [TestMethod]
    public async Task Execute_WhenReaderThrows_ReturnsFailed()
    {
        var profile = CreateProfile();
        var store = new StubProfileStore(profile);
        var reader = new StubRateLimitReader(
            new InvalidOperationException("read failed"));
        var useCase = CreateUseCase(store, reader);

        var result = await useCase.ExecuteAsync(
            profile.Id,
            keepAlive: false,
            CancellationToken.None);

        Assert.AreEqual(
            ProfileRateLimitStatus.Failed,
            result.Status);
        Assert.IsNull(result.FiveHourLimit);
        Assert.IsNull(result.WeeklyLimit);
    }

    private static RefreshProfileRateLimitUseCase CreateUseCase(
        IProfileStore store,
        IProfileRateLimitReader reader) =>
        new(
            store,
            reader,
            new ProfileOperationCoordinator());

    private static Profile CreateProfile() =>
        new(
            ProfileId.New(),
            ProfileName.Create("Work"));

    private sealed class StubProfileStore : IProfileStore
    {
        private readonly Profile _profile;
        private readonly byte[] _credential =
            "original-credential"u8.ToArray();

        public StubProfileStore(Profile profile)
        {
            _profile = profile;
        }

        public byte[]? ReplacedCredential { get; private set; }

        public Task<ProfileStoreReadResult> ReadAllAsync(
            CancellationToken cancellationToken) =>
            Task.FromResult(
                new ProfileStoreReadResult(
                    [_profile],
                    []));

        public Task SaveAsync(
            Profile profile,
            ReadOnlyMemory<byte> credential,
            CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task<byte[]> ReadCredentialAsync(
            ProfileId profileId,
            CancellationToken cancellationToken) =>
            Task.FromResult(_credential.ToArray());

        public Task ReplaceCredentialAsync(
            ProfileId profileId,
            ReadOnlyMemory<byte> credential,
            CancellationToken cancellationToken)
        {
            ReplacedCredential = credential.ToArray();
            return Task.CompletedTask;
        }

        public Task DeleteAsync(
            ProfileId profileId,
            CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }

    private sealed class StubRateLimitReader
        : IProfileRateLimitReader
    {
        private readonly ProfileRateLimitReadResult _result;
        private readonly Exception? _exception;

        public StubRateLimitReader(
            ProfileRateLimitReadResult result)
        {
            _result = result;
        }

        public StubRateLimitReader(Exception exception)
        {
            _result = new ProfileRateLimitReadResult(
                ProfileRateLimitStatus.Failed,
                []);
            _exception = exception;
        }

        public bool LastKeepAlive { get; private set; }

        public Task<ProfileRateLimitReadResult> ReadAsync(
            ProfileId profileId,
            ReadOnlyMemory<byte> credential,
            bool keepAlive,
            CancellationToken cancellationToken)
        {
            LastKeepAlive = keepAlive;
            if (_exception is not null)
            {
                throw _exception;
            }

            return Task.FromResult(_result);
        }

        public void StopActiveSession()
        {
        }

        public void Dispose()
        {
        }
    }
}
