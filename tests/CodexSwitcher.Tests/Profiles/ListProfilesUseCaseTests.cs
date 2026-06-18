using CodexSwitcher.Core.Profiles;

namespace CodexSwitcher.Tests.Profiles;

[TestClass]
public sealed class ListProfilesUseCaseTests
{
    [TestMethod]
    public async Task ExecuteAsync_ReturnsProfilesAndSafeIssues()
    {
        var expected = new ProfileStoreReadResult(
            [
                new Profile(
                    ProfileId.New(),
                    ProfileName.Create("Work"))
            ],
            [
                new ProfileStoreIssue(
                    ProfileStoreIssueCode.CorruptMetadata)
            ]);
        var useCase = new ListProfilesUseCase(
            new StubProfileStore(expected));

        var result = await useCase.ExecuteAsync(
            CancellationToken.None);

        Assert.AreSame(expected, result);
        Assert.AreEqual(
            ProfileStoreIssueCode.CorruptMetadata,
            result.Issues.Single().Code);
    }

    private sealed class StubProfileStore : IProfileStore
    {
        private readonly ProfileStoreReadResult _result;

        public StubProfileStore(ProfileStoreReadResult result)
        {
            _result = result;
        }

        public Task<ProfileStoreReadResult> ReadAllAsync(
            CancellationToken cancellationToken)
        {
            return Task.FromResult(_result);
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
