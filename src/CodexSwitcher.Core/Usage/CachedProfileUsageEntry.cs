using CodexSwitcher.Core.Profiles;

namespace CodexSwitcher.Core.Usage;

/// <summary>
/// 표시용 사용량 캐시 항목. 인증 원문·토큰은 포함하지 않는다.
/// </summary>
public sealed record CachedProfileUsageEntry(
    ProfileId ProfileId,
    ProfileRateLimitStatus Status,
    RateLimitWindow? FiveHourLimit,
    RateLimitWindow? WeeklyLimit,
    DateTimeOffset? LastSuccessfulAt,
    DateTimeOffset CapturedAt)
{
    public ProfileRateLimitSnapshot ToSnapshot() =>
        new(
            ProfileId,
            Status,
            FiveHourLimit,
            WeeklyLimit,
            LastSuccessfulAt);

    public static CachedProfileUsageEntry FromSnapshot(
        ProfileRateLimitSnapshot snapshot,
        DateTimeOffset capturedAt) =>
        new(
            snapshot.ProfileId,
            snapshot.Status,
            snapshot.FiveHourLimit,
            snapshot.WeeklyLimit,
            snapshot.LastSuccessfulAt,
            capturedAt);
}
