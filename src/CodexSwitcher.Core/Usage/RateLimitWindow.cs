namespace CodexSwitcher.Core.Usage;

public sealed record RateLimitWindow(
    int UsedPercent,
    long? WindowDurationMinutes,
    DateTimeOffset? ResetsAt)
{
    public int RemainingPercent =>
        Math.Clamp(100 - UsedPercent, 0, 100);

    public RateLimitDisplayLevel DisplayLevel =>
        RemainingPercent switch
        {
            0 => RateLimitDisplayLevel.Dead,
            < 20 => RateLimitDisplayLevel.Danger,
            < 40 => RateLimitDisplayLevel.Warning,
            _ => RateLimitDisplayLevel.Healthy
        };
}
