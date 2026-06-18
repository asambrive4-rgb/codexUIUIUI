namespace CodexSwitcher.Core.Profiles;

public sealed record CreateProfileLoginResult(
    CreateProfileLoginStatus Status,
    Profile? Profile = null);
