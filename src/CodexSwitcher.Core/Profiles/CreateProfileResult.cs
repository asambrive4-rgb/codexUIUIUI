namespace CodexSwitcher.Core.Profiles;

public sealed record CreateProfileResult(
    CreateProfileStatus Status,
    Profile? Profile = null);
