using System.Security.Cryptography;

namespace CodexSwitcher.Core.Profiles;

public sealed class DeleteProfileUseCase
{
    private readonly IProfileStore _profileStore;
    private readonly IAuthenticationSession _authenticationSession;
    private readonly ICodexLoginController _codexController;
    private readonly ProfileOperationCoordinator _operationCoordinator;

    public DeleteProfileUseCase(
        IProfileStore profileStore,
        IAuthenticationSession authenticationSession,
        ICodexLoginController codexController,
        ProfileOperationCoordinator operationCoordinator)
    {
        _profileStore = profileStore;
        _authenticationSession = authenticationSession;
        _codexController = codexController;
        _operationCoordinator = operationCoordinator;
    }

    public async Task<DeleteProfileResult> ExecuteAsync(
        ProfileId profileId,
        CancellationToken cancellationToken)
    {
        using var operation =
            await _operationCoordinator.TryEnterAsync(cancellationToken);
        if (operation is null)
        {
            return Result(DeleteProfileStatus.Failed);
        }

        byte[]? currentCredential = null;

        try
        {
            if (await _authenticationSession.HasPendingRecoveryAsync(
                    cancellationToken))
            {
                return Result(DeleteProfileStatus.RecoveryRequired);
            }

            var profiles = await _profileStore.ReadAllAsync(
                cancellationToken);
            if (profiles.Profiles.All(profile => profile.Id != profileId))
            {
                return Result(DeleteProfileStatus.ProfileNotFound);
            }

            if (await _codexController.IsRunningAsync(cancellationToken))
            {
                currentCredential =
                    await _authenticationSession.ReadCurrentCredentialAsync(
                        cancellationToken);
                var activeProfileId =
                    await FindSingleMatchingProfileIdAsync(
                        profiles.Profiles,
                        currentCredential,
                        cancellationToken);

                if (activeProfileId is null)
                {
                    return Result(DeleteProfileStatus.RunningProfileUnknown);
                }

                if (activeProfileId == profileId)
                {
                    return Result(DeleteProfileStatus.ActiveProfileBlocked);
                }
            }

            try
            {
                await _profileStore.DeleteAsync(
                    profileId,
                    cancellationToken);
            }
            catch (Exception exception)
                when (exception is DirectoryNotFoundException or
                      FileNotFoundException)
            {
                return Result(DeleteProfileStatus.ProfileNotFound);
            }

            return Result(DeleteProfileStatus.Deleted);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return Result(DeleteProfileStatus.Failed);
        }
        finally
        {
            if (currentCredential is not null)
            {
                CryptographicOperations.ZeroMemory(currentCredential);
            }
        }
    }

    private async Task<ProfileId?> FindSingleMatchingProfileIdAsync(
        IReadOnlyList<Profile> profiles,
        byte[]? currentCredential,
        CancellationToken cancellationToken)
    {
        if (currentCredential is null)
        {
            return null;
        }

        ProfileId? match = null;
        foreach (var profile in profiles)
        {
            byte[]? storedCredential = null;
            try
            {
                storedCredential =
                    await _profileStore.ReadCredentialAsync(
                        profile.Id,
                        cancellationToken);
                if (!CredentialsEqual(
                        storedCredential,
                        currentCredential))
                {
                    continue;
                }

                if (match is not null)
                {
                    return null;
                }

                match = profile.Id;
            }
            catch (OperationCanceledException)
                when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                return null;
            }
            finally
            {
                if (storedCredential is not null)
                {
                    CryptographicOperations.ZeroMemory(storedCredential);
                }
            }
        }

        return match;
    }

    private static bool CredentialsEqual(
        byte[] expected,
        byte[]? actual)
    {
        return actual is not null &&
               expected.Length == actual.Length &&
               CryptographicOperations.FixedTimeEquals(
                   expected,
                   actual);
    }

    private static DeleteProfileResult Result(
        DeleteProfileStatus status) => new(status);
}
