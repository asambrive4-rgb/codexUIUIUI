namespace CodexSwitcher.Core.Profiles;

public sealed record ProfileStoreReadResult(
    IReadOnlyList<Profile> Profiles,
    IReadOnlyList<ProfileStoreIssue> Issues);
