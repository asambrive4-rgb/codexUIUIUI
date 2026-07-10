using CodexSwitcher.Core.Profiles;
using CodexSwitcher.Core.Usage;

namespace CodexSwitcher.Infrastructure.Usage;

/// <summary>
/// 테스트·기본용 인메모리 사용량 캐시. 디스크 I/O 없음.
/// </summary>
public sealed class MemoryProfileUsageSnapshotCache
    : IProfileUsageSnapshotCache
{
    private readonly object _gate = new();
    private readonly Dictionary<ProfileId, CachedProfileUsageEntry>
        _entries = [];

    public Task LoadAsync(CancellationToken cancellationToken) =>
        Task.CompletedTask;

    public bool TryGet(
        ProfileId profileId,
        out CachedProfileUsageEntry entry)
    {
        lock (_gate)
        {
            return _entries.TryGetValue(profileId, out entry!);
        }
    }

    public Task SetAsync(
        CachedProfileUsageEntry entry,
        CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            _entries[entry.ProfileId] = entry;
        }

        return Task.CompletedTask;
    }

    public Task RemoveAsync(
        ProfileId profileId,
        CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            _entries.Remove(profileId);
        }

        return Task.CompletedTask;
    }

    public Task RemoveMissingAsync(
        IReadOnlyCollection<ProfileId> keep,
        CancellationToken cancellationToken)
    {
        var keepSet = keep.ToHashSet();
        lock (_gate)
        {
            foreach (var id in _entries.Keys
                         .Where(id => !keepSet.Contains(id))
                         .ToArray())
            {
                _entries.Remove(id);
            }
        }

        return Task.CompletedTask;
    }
}
