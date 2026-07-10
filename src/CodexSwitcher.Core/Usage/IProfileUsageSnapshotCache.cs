using CodexSwitcher.Core.Profiles;

namespace CodexSwitcher.Core.Usage;

public interface IProfileUsageSnapshotCache
{
    Task LoadAsync(CancellationToken cancellationToken);

    bool TryGet(
        ProfileId profileId,
        out CachedProfileUsageEntry entry);

    Task SetAsync(
        CachedProfileUsageEntry entry,
        CancellationToken cancellationToken);

    Task RemoveAsync(
        ProfileId profileId,
        CancellationToken cancellationToken);

    Task RemoveMissingAsync(
        IReadOnlyCollection<ProfileId> keep,
        CancellationToken cancellationToken);
}
