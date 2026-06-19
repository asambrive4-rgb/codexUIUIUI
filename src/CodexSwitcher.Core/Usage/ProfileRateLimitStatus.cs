namespace CodexSwitcher.Core.Usage;

public enum ProfileRateLimitStatus
{
    Loading,
    Available,
    UnsupportedAuthentication,
    AuthenticationExpired,
    CodexUpdateRequired,
    Failed
}
