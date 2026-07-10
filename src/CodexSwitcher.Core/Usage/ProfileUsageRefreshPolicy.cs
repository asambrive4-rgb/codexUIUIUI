namespace CodexSwitcher.Core.Usage;

public static class ProfileUsageRefreshPolicy
{
    public static readonly TimeSpan ActiveRefreshInterval =
        TimeSpan.FromSeconds(10);
    public static readonly TimeSpan InactiveRefreshInterval =
        TimeSpan.FromMinutes(5);
    public static readonly TimeSpan ActiveFailureBackoff =
        TimeSpan.FromMinutes(1);
    public static readonly TimeSpan InactiveFailureCacheTtl =
        TimeSpan.FromMinutes(1);

    /// <summary>
    /// 주기 틱에서 한 번에 수행할 최대 Codex probe 수.
    /// 활성 due가 있으면 활성 우선 1건, 없으면 비활성 1건.
    /// </summary>
    public const int ScheduledProbeBudget = 1;

    public static bool IsDue(
        DateTimeOffset now,
        DateTimeOffset? lastAttempt,
        bool isActive,
        int consecutiveFailures)
    {
        if (lastAttempt is null)
        {
            return true;
        }

        var interval = isActive
            ? consecutiveFailures >= 3
                ? ActiveFailureBackoff
                : ActiveRefreshInterval
            : InactiveRefreshInterval;
        return now - lastAttempt.Value >= interval;
    }

    public static TimeSpan GetCacheTtl(
        ProfileRateLimitStatus status,
        bool isActive)
    {
        if (status == ProfileRateLimitStatus.Available)
        {
            return isActive
                ? ActiveRefreshInterval
                : InactiveRefreshInterval;
        }

        if (status == ProfileRateLimitStatus.Loading)
        {
            return TimeSpan.Zero;
        }

        return isActive
            ? ActiveFailureBackoff
            : InactiveFailureCacheTtl;
    }

    public static bool IsCacheFresh(
        DateTimeOffset now,
        DateTimeOffset capturedAt,
        ProfileRateLimitStatus status,
        bool isActive)
    {
        var ttl = GetCacheTtl(status, isActive);
        if (ttl <= TimeSpan.Zero)
        {
            return false;
        }

        return now - capturedAt < ttl;
    }
}
