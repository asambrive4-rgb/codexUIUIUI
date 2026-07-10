using CodexSwitcher.Core.Profiles;
using CodexSwitcher.Core.Usage;
using CodexSwitcher.Infrastructure.Usage;

namespace CodexSwitcher.Tests.Usage;

[TestClass]
public sealed class FileProfileUsageSnapshotCacheTests
{
    [TestMethod]
    public async Task SetAndLoad_RoundTripsDisplayFieldsWithoutSecrets()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            $"codex-usage-cache-{Guid.NewGuid():N}.json");
        try
        {
            var profileId = ProfileId.New();
            var capturedAt = new DateTimeOffset(
                2026, 7, 10, 12, 0, 0, TimeSpan.Zero);
            var writer = new FileProfileUsageSnapshotCache(path);
            await writer.SetAsync(
                new CachedProfileUsageEntry(
                    profileId,
                    ProfileRateLimitStatus.Available,
                    new RateLimitWindow(12, 300, capturedAt.AddHours(1)),
                    new RateLimitWindow(40, 10_080, capturedAt.AddDays(1)),
                    capturedAt,
                    capturedAt),
                CancellationToken.None);

            var json = await File.ReadAllTextAsync(path);
            Assert.DoesNotContain("token", json, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("credential", json, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("auth.json", json, StringComparison.OrdinalIgnoreCase);

            var reader = new FileProfileUsageSnapshotCache(path);
            await reader.LoadAsync(CancellationToken.None);
            Assert.IsTrue(reader.TryGet(profileId, out var entry));
            Assert.AreEqual(ProfileRateLimitStatus.Available, entry.Status);
            Assert.AreEqual(12, entry.FiveHourLimit?.UsedPercent);
            Assert.AreEqual(40, entry.WeeklyLimit?.UsedPercent);
            Assert.AreEqual(capturedAt, entry.CapturedAt);
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    [TestMethod]
    public async Task RemoveMissing_DropsDeletedProfiles()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            $"codex-usage-cache-{Guid.NewGuid():N}.json");
        try
        {
            var keep = ProfileId.New();
            var drop = ProfileId.New();
            var cache = new FileProfileUsageSnapshotCache(path);
            var now = DateTimeOffset.UtcNow;
            await cache.SetAsync(
                new CachedProfileUsageEntry(
                    keep,
                    ProfileRateLimitStatus.Available,
                    null,
                    null,
                    now,
                    now),
                CancellationToken.None);
            await cache.SetAsync(
                new CachedProfileUsageEntry(
                    drop,
                    ProfileRateLimitStatus.Failed,
                    null,
                    null,
                    null,
                    now),
                CancellationToken.None);

            await cache.RemoveMissingAsync([keep], CancellationToken.None);

            Assert.IsTrue(cache.TryGet(keep, out _));
            Assert.IsFalse(cache.TryGet(drop, out _));
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }
}
