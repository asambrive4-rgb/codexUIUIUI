namespace CodexSwitcher.Core.Profiles;

public sealed class CreateProfileUseCase
{
    private readonly IProfileStore _profileStore;

    public CreateProfileUseCase(IProfileStore profileStore)
    {
        _profileStore = profileStore;
    }

    public async Task<CreateProfileResult> ExecuteAsync(
        string name,
        ReadOnlyMemory<byte> credential,
        CancellationToken cancellationToken)
    {
        ProfileName profileName;

        try
        {
            profileName = ProfileName.Create(name);
        }
        catch (ArgumentException)
        {
            return new CreateProfileResult(CreateProfileStatus.InvalidName);
        }

        var stored = await _profileStore.ReadAllAsync(cancellationToken);
        if (stored.Issues.Count > 0)
        {
            return new CreateProfileResult(
                CreateProfileStatus.StorageNeedsAttention);
        }

        if (stored.Profiles.Any(
                profile => StringComparer.OrdinalIgnoreCase.Equals(
                    profile.Name.Value,
                    profileName.Value)))
        {
            return new CreateProfileResult(CreateProfileStatus.DuplicateName);
        }

        var profile = new Profile(ProfileId.New(), profileName);
        await _profileStore.SaveAsync(
            profile,
            credential,
            cancellationToken);

        return new CreateProfileResult(
            CreateProfileStatus.Created,
            profile);
    }
}
