namespace CodexSwitcher.Core.Usage;

public static class ProfileUsageRefreshPolicy
{
    public static readonly TimeSpan ActiveRefreshInterval =
        TimeSpan.FromSeconds(10);
    public static readonly TimeSpan InactiveRefreshInterval =
        TimeSpan.FromMinutes(5);
    public static readonly TimeSpan ActiveFailureBackoff =
        TimeSpan.FromMinutes(1);

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
}
