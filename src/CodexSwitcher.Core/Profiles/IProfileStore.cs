namespace CodexSwitcher.Core.Profiles;

public interface IProfileStore
{
    Task<ProfileStoreReadResult> ReadAllAsync(
        CancellationToken cancellationToken);

    Task SaveAsync(
        Profile profile,
        ReadOnlyMemory<byte> credential,
        CancellationToken cancellationToken);

    Task<byte[]> ReadCredentialAsync(
        ProfileId profileId,
        CancellationToken cancellationToken);

    Task DeleteAsync(
        ProfileId profileId,
        CancellationToken cancellationToken);
}
