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

    private sealed class Fixture
    {
        public Fixture()
        {
            Store = new StubProfileStore();
            Authentication = new StubAuthenticationSession();
            Codex = new StubCodexController();
            Identity = new StubCredentialIdentityReader();
            UseCase = new GetProfileRuntimeStateUseCase(
                Store,
                Authentication,
                Codex,
                Identity);
        }

        public StubProfileStore Store { get; }

        public StubAuthenticationSession Authentication { get; }

        public StubCodexController Codex { get; }

        public StubCredentialIdentityReader Identity { get; }

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

        public Task<ProfileStoreReadResult> ReadAllAsync(
            CancellationToken cancellationToken)
        {
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

        public Task<bool> IsRunningAsync(
            CancellationToken cancellationToken)
        {
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
