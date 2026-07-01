using CodexSwitcher.Core.Profiles;

namespace CodexSwitcher.Tests.Profiles;

[TestClass]
public sealed class GetProfileRuntimeStateUseCaseTests
{
    [TestMethod]
    public async Task Execute_WhenCodexIsStopped_DoesNotInspectCredential()
    {
        var fixture = new Fixture();
        fixture.Codex.IsRunning = false;

        var result = await fixture.UseCase.ExecuteAsync(
            CancellationToken.None);

        Assert.AreEqual(
            ProfileRuntimeStatus.Stopped,
            result.Status);
        Assert.AreEqual(0, fixture.Authentication.ReadCount);
    }

    [TestMethod]
    public async Task Execute_WithExactlyOneCredentialMatch_ReturnsKnownProfile()
    {
        var fixture = new Fixture();
        var matching = fixture.AddProfile(
            "Work",
            "current"u8.ToArray());
        _ = fixture.AddProfile(
            "Personal",
            "other"u8.ToArray());
        fixture.Authentication.CurrentCredential =
            "current"u8.ToArray();

        var result = await fixture.UseCase.ExecuteAsync(
            CancellationToken.None);

        Assert.AreEqual(
            ProfileRuntimeStatus.RunningKnownProfile,
            result.Status);
        Assert.AreEqual(matching.Id, result.ActiveProfileId);
    }

    [TestMethod]
    public async Task Execute_WithNoCredentialMatch_ReturnsUnknown()
    {
        var fixture = new Fixture();
        _ = fixture.AddProfile(
            "Work",
            "stored"u8.ToArray());
        fixture.Authentication.CurrentCredential =
            "different"u8.ToArray();

        var result = await fixture.UseCase.ExecuteAsync(
            CancellationToken.None);

        Assert.AreEqual(
            ProfileRuntimeStatus.RunningUnknownProfile,
            result.Status);
        Assert.IsNull(result.ActiveProfileId);
    }

    [TestMethod]
    public async Task Execute_WithMatchingAccountId_ReturnsKnownProfile()
    {
        var fixture = new Fixture();
        var matching = fixture.AddProfile(
            "Work",
            "stored-refreshed-token"u8.ToArray());
        _ = fixture.AddProfile(
            "Personal",
            "other-token"u8.ToArray());
        fixture.Authentication.CurrentCredential =
            "current-refreshed-token"u8.ToArray();
        fixture.Identity.AccountIds["current-refreshed-token"] =
            "acct_1";
        fixture.Identity.AccountIds["stored-refreshed-token"] =
            "acct_1";
        fixture.Identity.AccountIds["other-token"] =
            "acct_2";

        var result = await fixture.UseCase.ExecuteAsync(
            CancellationToken.None);

        Assert.AreEqual(
            ProfileRuntimeStatus.RunningKnownProfile,
            result.Status);
        Assert.AreEqual(matching.Id, result.ActiveProfileId);
    }

    [TestMethod]
    public async Task Execute_WithDuplicateCredentialMatches_ReturnsUnknown()
    {
        var fixture = new Fixture();
        _ = fixture.AddProfile(
            "Work",
            "same"u8.ToArray());
        _ = fixture.AddProfile(
            "Personal",
            "same"u8.ToArray());
        fixture.Authentication.CurrentCredential =
            "same"u8.ToArray();

        var result = await fixture.UseCase.ExecuteAsync(
            CancellationToken.None);

        Assert.AreEqual(
            ProfileRuntimeStatus.RunningUnknownProfile,
            result.Status);
    }

    [TestMethod]
    public async Task Execute_WithoutCurrentCredential_ReturnsUnknown()
    {
        var fixture = new Fixture();
        _ = fixture.AddProfile(
            "Work",
            "stored"u8.ToArray());
        fixture.Authentication.CurrentCredential = null;

        var result = await fixture.UseCase.ExecuteAsync(
            CancellationToken.None);

        Assert.AreEqual(
            ProfileRuntimeStatus.RunningUnknownProfile,
            result.Status);
    }

    [TestMethod]
    public async Task Execute_WhenRunningStateIsCached_DoesNotRepeatCredentialReads()
    {
        var fixture = new Fixture();
        var matching = fixture.AddProfile(
            "Work",
            "current"u8.ToArray());
        fixture.Authentication.CurrentCredential =
            "current"u8.ToArray();

        var first = await fixture.UseCase.ExecuteAsync(
            CancellationToken.None);
        fixture.Authentication.CurrentCredential =
            "changed-outside-cache"u8.ToArray();
        var second = await fixture.UseCase.ExecuteAsync(
            CancellationToken.None);

        Assert.AreEqual(
            ProfileRuntimeStatus.RunningKnownProfile,
            first.Status);
        Assert.AreEqual(
            ProfileRuntimeStatus.RunningKnownProfile,
            second.Status);
        Assert.AreEqual(matching.Id, second.ActiveProfileId);
        Assert.AreEqual(2, fixture.Codex.IsRunningReadCount);
        Assert.AreEqual(1, fixture.Authentication.ReadCount);
        Assert.AreEqual(1, fixture.Store.ReadAllCount);
        Assert.AreEqual(1, fixture.Store.ReadCredentialCount);
    }

    [TestMethod]
    public async Task Execute_WithForceRefresh_RechecksCachedRunningState()
    {
        var fixture = new Fixture();
        _ = fixture.AddProfile(
            "Work",
            "current"u8.ToArray());
        fixture.Authentication.CurrentCredential =
            "current"u8.ToArray();
        _ = await fixture.UseCase.ExecuteAsync(
            CancellationToken.None);

        fixture.Authentication.CurrentCredential =
            "changed"u8.ToArray();
        var refreshed = await fixture.UseCase.ExecuteAsync(
            forceRefresh: true,
            CancellationToken.None);

        Assert.AreEqual(
            ProfileRuntimeStatus.RunningUnknownProfile,
            refreshed.Status);
        Assert.AreEqual(2, fixture.Authentication.ReadCount);
        Assert.AreEqual(2, fixture.Store.ReadAllCount);
        Assert.AreEqual(2, fixture.Store.ReadCredentialCount);
    }

    [TestMethod]
    public async Task Execute_AfterCacheExpires_RechecksRunningState()
    {
        var fixture = new Fixture();
        _ = fixture.AddProfile(
            "Work",
            "current"u8.ToArray());
        fixture.Authentication.CurrentCredential =
            "current"u8.ToArray();
        _ = await fixture.UseCase.ExecuteAsync(
            CancellationToken.None);

        fixture.Authentication.CurrentCredential =
            "changed"u8.ToArray();
        fixture.Time.Advance(TimeSpan.FromSeconds(6));
        var refreshed = await fixture.UseCase.ExecuteAsync(
            CancellationToken.None);

        Assert.AreEqual(
            ProfileRuntimeStatus.RunningUnknownProfile,
            refreshed.Status);
        Assert.AreEqual(2, fixture.Authentication.ReadCount);
        Assert.AreEqual(2, fixture.Store.ReadAllCount);
        Assert.AreEqual(2, fixture.Store.ReadCredentialCount);
    }

    [TestMethod]
    public async Task Execute_WhenCodexStops_ReturnsStoppedWithoutCredentialRead()
    {
        var fixture = new Fixture();
        _ = fixture.AddProfile(
            "Work",
            "current"u8.ToArray());
        fixture.Authentication.CurrentCredential =
            "current"u8.ToArray();
        _ = await fixture.UseCase.ExecuteAsync(
            CancellationToken.None);

        fixture.Codex.IsRunning = false;
        var stopped = await fixture.UseCase.ExecuteAsync(
            CancellationToken.None);

        Assert.AreEqual(
            ProfileRuntimeStatus.Stopped,
            stopped.Status);
        Assert.AreEqual(1, fixture.Authentication.ReadCount);
        Assert.AreEqual(1, fixture.Store.ReadAllCount);
        Assert.AreEqual(1, fixture.Store.ReadCredentialCount);
    }

    [TestMethod]
    public async Task Execute_WhenCodexRestartsAfterStopped_RechecksRunningState()
    {
        var fixture = new Fixture();
        _ = fixture.AddProfile(
            "Work",
            "current"u8.ToArray());
        fixture.Authentication.CurrentCredential =
            "current"u8.ToArray();
        _ = await fixture.UseCase.ExecuteAsync(
            CancellationToken.None);

        fixture.Codex.IsRunning = false;
        _ = await fixture.UseCase.ExecuteAsync(
            CancellationToken.None);
        fixture.Codex.IsRunning = true;
        fixture.Authentication.CurrentCredential =
            "changed"u8.ToArray();
        var restarted = await fixture.UseCase.ExecuteAsync(
            CancellationToken.None);

        Assert.AreEqual(
            ProfileRuntimeStatus.RunningUnknownProfile,
            restarted.Status);
        Assert.AreEqual(2, fixture.Authentication.ReadCount);
        Assert.AreEqual(2, fixture.Store.ReadAllCount);
        Assert.AreEqual(2, fixture.Store.ReadCredentialCount);
    }

    private sealed class Fixture
    {
        public Fixture()
        {
            Store = new StubProfileStore();
            Authentication = new StubAuthenticationSession();
            Codex = new StubCodexController();
            Identity = new StubCredentialIdentityReader();
            Time = new ManualTimeProvider();
            UseCase = new GetProfileRuntimeStateUseCase(
                Store,
                Authentication,
                Codex,
                Identity,
                Time);
        }

        public StubProfileStore Store { get; }

        public StubAuthenticationSession Authentication { get; }

        public StubCodexController Codex { get; }

        public StubCredentialIdentityReader Identity { get; }

        public ManualTimeProvider Time { get; }

        public GetProfileRuntimeStateUseCase UseCase { get; }

        public Profile AddProfile(
            string name,
            byte[] credential)
        {
            var profile = new Profile(
                ProfileId.New(),
                ProfileName.Create(name));
            Store.Profiles.Add(profile);
            Store.Credentials[profile.Id] = credential;
            return profile;
        }
    }

    private sealed class StubCredentialIdentityReader
        : ICredentialIdentityReader
    {
        public Dictionary<string, string> AccountIds { get; } = [];

        public string? TryReadAccountId(
            ReadOnlySpan<byte> credential)
        {
            var text = System.Text.Encoding.UTF8.GetString(credential);
            return AccountIds.GetValueOrDefault(text);
        }
    }

    private sealed class StubProfileStore : IProfileStore
    {
        public List<Profile> Profiles { get; } = [];

        public Dictionary<ProfileId, byte[]> Credentials { get; } = [];

        public int ReadAllCount { get; private set; }

        public int ReadCredentialCount { get; private set; }

        public Task<ProfileStoreReadResult> ReadAllAsync(
            CancellationToken cancellationToken)
        {
            ReadAllCount++;
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
            ReadCredentialCount++;
            return Task.FromResult(
                Credentials[profileId].ToArray());
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
        public int ReadCount { get; private set; }

        public byte[]? CurrentCredential { get; set; }

        public Task<bool> HasPendingRecoveryAsync(
            CancellationToken cancellationToken)
        {
            return Task.FromResult(false);
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
            ReadCount++;
            return Task.FromResult(
                CurrentCredential?.ToArray());
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
        public bool IsRunning { get; set; } = true;

        public int IsRunningReadCount { get; private set; }

        public Task<bool> IsRunningAsync(
            CancellationToken cancellationToken)
        {
            IsRunningReadCount++;
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

    private sealed class ManualTimeProvider : TimeProvider
    {
        private DateTimeOffset _utcNow =
            new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => _utcNow;

        public void Advance(TimeSpan offset)
        {
            _utcNow += offset;
        }
    }
}
