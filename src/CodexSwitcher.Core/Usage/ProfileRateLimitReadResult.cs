namespace CodexSwitcher.Core.Usage;

public sealed record ProfileRateLimitReadResult(
    ProfileRateLimitStatus Status,
    IReadOnlyList<RateLimitWindow> Windows,
    byte[]? RefreshedCredential = null);
