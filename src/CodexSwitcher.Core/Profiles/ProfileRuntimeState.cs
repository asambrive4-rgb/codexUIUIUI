namespace CodexSwitcher.Core.Profiles;

public sealed record ProfileRuntimeState(
    ProfileRuntimeStatus Status,
    ProfileId? ActiveProfileId = null);
