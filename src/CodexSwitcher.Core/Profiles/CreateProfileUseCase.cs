namespace CodexSwitcher.Core.Profiles;

public sealed class CreateProfileUseCase
{
    private readonly IProfileStore _profileStore;
    private readonly ProfileCreationValidator _validator;

    public CreateProfileUseCase(IProfileStore profileStore)
    {
        _profileStore = profileStore;
        _validator = new ProfileCreationValidator(profileStore);
    }

    public async Task<CreateProfileResult> ExecuteAsync(
        string name,
        ReadOnlyMemory<byte> credential,
        CancellationToken cancellationToken)
    {
        var validation = await _validator.ValidateAsync(
            name,
            cancellationToken);
        if (validation.Status != ProfileCreationValidationStatus.Valid)
        {
            return new CreateProfileResult(
                validation.Status switch
                {
                    ProfileCreationValidationStatus.InvalidName =>
                        CreateProfileStatus.InvalidName,
                    ProfileCreationValidationStatus.DuplicateName =>
                        CreateProfileStatus.DuplicateName,
                    _ => CreateProfileStatus.StorageNeedsAttention
                });
        }

        var profile = new Profile(
            ProfileId.New(),
            validation.Name!);
        await _profileStore.SaveAsync(
            profile,
            credential,
            cancellationToken);

        return new CreateProfileResult(
            CreateProfileStatus.Created,
            profile);
    }
}
