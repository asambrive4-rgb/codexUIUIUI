namespace CodexSwitcher.Core.Profiles;

internal sealed record ProfileCreationValidationResult(
    ProfileCreationValidationStatus Status,
    ProfileName? Name = null);
