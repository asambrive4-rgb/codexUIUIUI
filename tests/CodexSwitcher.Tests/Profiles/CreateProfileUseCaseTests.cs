using System.Text;
using CodexSwitcher.Core.Profiles;

namespace CodexSwitcher.Tests.Profiles;

[TestClass]
public sealed class CreateProfileUseCaseTests
{
    [TestMethod]
    [DataRow("")]
    [DataRow("   ")]
    public async Task ExecuteAsync_WithInvalidName_DoesNotSave(
        string name)
    {
        var store = new StubProfileStore();
        var useCase = new CreateProfileUseCase(store);

        var result = await useCase.ExecuteAsync(
            name,
            "credential"u8.ToArray(),
            CancellationToken.None);

        Assert.AreEqual(CreateProfileStatus.InvalidName, result.Status);
        Assert.IsEmpty(store.SavedProfiles);
    }

    [TestMethod]
    public async Task ExecuteAsync_WithCaseOnlyDuplicate_RejectsName()
    {
        var store = new StubProfileStore();
        store.Profiles.Add(
            new Profile(
                ProfileId.New(),
                ProfileName.Create("Work")));
        var useCase = new CreateProfileUseCase(store);

        var result = await useCase.ExecuteAsync(
            "work",
            "credential"u8.ToArray(),
            CancellationToken.None);

        Assert.AreEqual(CreateProfileStatus.DuplicateName, result.Status);
        Assert.IsEmpty(store.SavedProfiles);
    }

    [TestMethod]
    public async Task ExecuteAsync_WhenStoreHasIssues_BlocksCreation()
    {
        var store = new StubProfileStore();
        store.Issues.Add(
            new ProfileStoreIssue(
                ProfileStoreIssueCode.CorruptMetadata));
        var useCase = new CreateProfileUseCase(store);

        var result = await useCase.ExecuteAsync(
            "Work",
            "credential"u8.ToArray(),
            CancellationToken.None);

        Assert.AreEqual(
            CreateProfileStatus.StorageNeedsAttention,
            result.Status);
        Assert.IsEmpty(store.SavedProfiles);
    }

    [TestMethod]
    public async Task ExecuteAsync_WithDistinctNames_CreatesUniqueIds()
    {
        var store = new StubProfileStore();
        var useCase = new CreateProfileUseCase(store);

        var first = await useCase.ExecuteAsync(
            " Work ",
            Encoding.UTF8.GetBytes("first"),
            CancellationToken.None);
        var second = await useCase.ExecuteAsync(
            "Personal",
            Encoding.UTF8.GetBytes("second"),
            CancellationToken.None);

        Assert.AreEqual(CreateProfileStatus.Created, first.Status);
        Assert.AreEqual(CreateProfileStatus.Created, second.Status);
        Assert.IsNotNull(first.Profile);
        Assert.IsNotNull(second.Profile);
        Assert.AreEqual("Work", first.Profile.Name.Value);
        Assert.AreNotEqual(first.Profile.Id, second.Profile.Id);
        Assert.HasCount(2, store.SavedProfiles);
    }

    [TestMethod]
    public async Task ExecuteAsync_WhenCanceled_PropagatesCancellation()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var useCase = new CreateProfileUseCase(
            new CancelingProfileStore());

        await Assert.ThrowsExactlyAsync<TaskCanceledException>(
            () => useCase.ExecuteAsync(
                "Work",
                "credential"u8.ToArray(),
                cancellation.Token));
    }

    private sealed class StubProfileStore : IProfileStore
    {
        public List<Profile> Profiles { get; } = [];

        public List<ProfileStoreIssue> Issues { get; } = [];

        public List<Profile> SavedProfiles { get; } = [];

        public Task<ProfileStoreReadResult> ReadAllAsync(
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(
                new ProfileStoreReadResult(
                    Profiles.ToArray(),
                    Issues.ToArray()));
        }

        public Task SaveAsync(
            Profile profile,
            ReadOnlyMemory<byte> credential,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SavedProfiles.Add(profile);
            Profiles.Add(profile);
            return Task.CompletedTask;
        }

        public Task<byte[]> ReadCredentialAsync(
            ProfileId profileId,
            CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task DeleteAsync(
            ProfileId profileId,
            CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }
    }

    private sealed class CancelingProfileStore : IProfileStore
    {
        public Task<ProfileStoreReadResult> ReadAllAsync(
            CancellationToken cancellationToken)
        {
            return Task.FromCanceled<ProfileStoreReadResult>(
                cancellationToken);
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
            throw new NotSupportedException();
        }

        public Task DeleteAsync(
            ProfileId profileId,
            CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }
    }
}
