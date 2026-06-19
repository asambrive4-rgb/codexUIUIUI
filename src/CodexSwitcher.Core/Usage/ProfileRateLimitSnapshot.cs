using CodexSwitcher.Core.Profiles;

namespace CodexSwitcher.Core.Usage;

public sealed record ProfileRateLimitSnapshot(
    ProfileId ProfileId,
    ProfileRateLimitStatus Status,
    RateLimitWindow? FiveHourLimit = null,
    RateLimitWindow? WeeklyLimit = null,
    DateTimeOffset? LastSuccessfulAt = null);
