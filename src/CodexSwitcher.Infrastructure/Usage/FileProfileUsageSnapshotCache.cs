using System.Text.Json;
using System.Text.Json.Serialization;
using CodexSwitcher.Core.Profiles;
using CodexSwitcher.Core.Usage;
using CodexSwitcher.Infrastructure.Profiles;

namespace CodexSwitcher.Infrastructure.Usage;

/// <summary>
/// 표시용 사용량 스냅샷 캐시 (메모리 + 디스크).
/// 인증 원문·토큰은 저장하지 않는다.
/// </summary>
public sealed class FileProfileUsageSnapshotCache
    : IProfileUsageSnapshotCache
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly string _filePath;
    private readonly object _gate = new();
    private readonly SemaphoreSlim _persistGate = new(1, 1);
    private readonly Dictionary<ProfileId, CachedProfileUsageEntry>
        _entries = [];

    public FileProfileUsageSnapshotCache()
        : this(CreateDefaultPath())
    {
    }

    public FileProfileUsageSnapshotCache(string filePath)
    {
        _filePath = filePath;
    }

    public async Task LoadAsync(CancellationToken cancellationToken)
    {
        var loaded = await Task.Run(
                ReadFromDisk,
                cancellationToken)
            .ConfigureAwait(false);

        lock (_gate)
        {
            _entries.Clear();
            foreach (var entry in loaded)
            {
                _entries[entry.ProfileId] = entry;
            }
        }
    }

    public bool TryGet(
        ProfileId profileId,
        out CachedProfileUsageEntry entry)
    {
        lock (_gate)
        {
            return _entries.TryGetValue(profileId, out entry!);
        }
    }

    public async Task SetAsync(
        CachedProfileUsageEntry entry,
        CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            _entries[entry.ProfileId] = entry;
        }

        await PersistAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task RemoveAsync(
        ProfileId profileId,
        CancellationToken cancellationToken)
    {
        var changed = false;
        lock (_gate)
        {
            changed = _entries.Remove(profileId);
        }

        if (changed)
        {
            await PersistAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task RemoveMissingAsync(
        IReadOnlyCollection<ProfileId> keep,
        CancellationToken cancellationToken)
    {
        var keepSet = keep.ToHashSet();
        var changed = false;
        lock (_gate)
        {
            foreach (var id in _entries.Keys
                         .Where(id => !keepSet.Contains(id))
                         .ToArray())
            {
                _entries.Remove(id);
                changed = true;
            }
        }

        if (changed)
        {
            await PersistAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task PersistAsync(CancellationToken cancellationToken)
    {
        CachedProfileUsageEntry[] snapshot;
        lock (_gate)
        {
            snapshot = _entries.Values.ToArray();
        }

        await _persistGate.WaitAsync(cancellationToken)
            .ConfigureAwait(false);
        try
        {
            await WriteToDiskAsync(snapshot, cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            _persistGate.Release();
        }
    }

    private IReadOnlyList<CachedProfileUsageEntry> ReadFromDisk()
    {
        try
        {
            if (!File.Exists(_filePath))
            {
                return [];
            }

            using var stream = File.OpenRead(_filePath);
            var dto = JsonSerializer.Deserialize<CacheFileDto>(
                stream,
                JsonOptions);
            if (dto?.Entries is null || dto.Version != 1)
            {
                return [];
            }

            var result = new List<CachedProfileUsageEntry>();
            foreach (var item in dto.Entries)
            {
                if (!ProfileId.TryParse(item.ProfileId, out var profileId))
                {
                    continue;
                }

                if (!Enum.TryParse<ProfileRateLimitStatus>(
                        item.Status,
                        ignoreCase: true,
                        out var status) ||
                    status == ProfileRateLimitStatus.Loading)
                {
                    continue;
                }

                result.Add(
                    new CachedProfileUsageEntry(
                        profileId,
                        status,
                        ToWindow(item.FiveHourLimit),
                        ToWindow(item.WeeklyLimit),
                        item.LastSuccessfulAt,
                        item.CapturedAt));
            }

            return result;
        }
        catch (Exception exception)
            when (exception is IOException or
                  UnauthorizedAccessException or
                  JsonException or
                  NotSupportedException)
        {
            return [];
        }
    }

    private async Task WriteToDiskAsync(
        IReadOnlyList<CachedProfileUsageEntry> entries,
        CancellationToken cancellationToken)
    {
        try
        {
            var dto = new CacheFileDto
            {
                Version = 1,
                Entries = entries
                    .Where(
                        entry =>
                            entry.Status !=
                            ProfileRateLimitStatus.Loading)
                    .Select(
                        entry => new CacheEntryDto
                        {
                            ProfileId = entry.ProfileId.ToString(),
                            Status = entry.Status.ToString(),
                            FiveHourLimit = ToDto(entry.FiveHourLimit),
                            WeeklyLimit = ToDto(entry.WeeklyLimit),
                            LastSuccessfulAt = entry.LastSuccessfulAt,
                            CapturedAt = entry.CapturedAt
                        })
                    .ToList()
            };

            var bytes = JsonSerializer.SerializeToUtf8Bytes(
                dto,
                JsonOptions);
            await AtomicFileWriter.WriteAsync(
                    _filePath,
                    bytes,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception exception)
            when (exception is IOException or
                  UnauthorizedAccessException or
                  JsonException)
        {
            // 캐시 저장 실패가 사용량 표시를 막으면 안 된다.
        }
    }

    private static string CreateDefaultPath()
    {
        var localAppData = Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(
            localAppData,
            "CodexAccountSwitcher",
            "usage-snapshot-cache.v1.json");
    }

    private static RateLimitWindow? ToWindow(WindowDto? dto)
    {
        if (dto is null)
        {
            return null;
        }

        return new RateLimitWindow(
            dto.UsedPercent,
            dto.WindowDurationMinutes,
            dto.ResetsAt);
    }

    private static WindowDto? ToDto(RateLimitWindow? window)
    {
        if (window is null)
        {
            return null;
        }

        return new WindowDto
        {
            UsedPercent = window.UsedPercent,
            WindowDurationMinutes = window.WindowDurationMinutes,
            ResetsAt = window.ResetsAt
        };
    }

    private sealed class CacheFileDto
    {
        public int Version { get; set; }

        public List<CacheEntryDto>? Entries { get; set; }
    }

    private sealed class CacheEntryDto
    {
        public string? ProfileId { get; set; }

        public string? Status { get; set; }

        public WindowDto? FiveHourLimit { get; set; }

        public WindowDto? WeeklyLimit { get; set; }

        public DateTimeOffset? LastSuccessfulAt { get; set; }

        public DateTimeOffset CapturedAt { get; set; }
    }

    private sealed class WindowDto
    {
        public int UsedPercent { get; set; }

        public long? WindowDurationMinutes { get; set; }

        public DateTimeOffset? ResetsAt { get; set; }
    }
}
