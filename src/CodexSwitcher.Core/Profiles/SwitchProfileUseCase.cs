using System.Security.Cryptography;

namespace CodexSwitcher.Core.Profiles;

public sealed class SwitchProfileUseCase
{
    private readonly IProfileStore _profileStore;
    private readonly IAuthenticationSession _authenticationSession;
    private readonly ICodexLoginController _codexController;
    private readonly ProfileOperationCoordinator _operationCoordinator;

    public SwitchProfileUseCase(
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

    public async Task<SwitchProfileResult> ExecuteAsync(
        ProfileId profileId,
        CancellationToken cancellationToken)
    {
        using var operation =
            await _operationCoordinator.TryEnterAsync(cancellationToken);
        if (operation is null)
        {
            return Result(SwitchProfileStatus.Failed);
        }

        byte[]? targetCredential = null;
        byte[]? currentCredential = null;
        byte[]? appliedCredential = null;
        var processConfirmedRunning = false;

        try
        {
            if (await _authenticationSession.HasPendingRecoveryAsync(
                    cancellationToken))
            {
                return Result(SwitchProfileStatus.RecoveryRequired);
            }

            var profiles = await _profileStore.ReadAllAsync(
                cancellationToken);
            var targetProfile = profiles.Profiles.FirstOrDefault(
                profile => profile.Id == profileId);
            if (targetProfile is null)
            {
                return Result(SwitchProfileStatus.ProfileNotFound);
            }

            try
            {
                targetCredential =
                    await _profileStore.ReadCredentialAsync(
                        profileId,
                        cancellationToken);
            }
            catch (Exception exception)
                when (exception is DirectoryNotFoundException or
                      FileNotFoundException)
            {
                return Result(SwitchProfileStatus.ProfileNotFound);
            }

            var isRunning =
                await _codexController.IsRunningAsync(cancellationToken);
            if (isRunning)
            {
                currentCredential =
                    await _authenticationSession.ReadCurrentCredentialAsync(
                        cancellationToken);
                var activeProfileId =
                    await FindSingleMatchingProfileIdAsync(
                        profiles.Profiles,
                        currentCredential,
                        cancellationToken);
                if (activeProfileId == profileId)
                {
                    return Result(SwitchProfileStatus.AlreadyRunningTarget);
                }

                if (!await _codexController.ForceStopAsync(
                        cancellationToken))
                {
                    return Result(SwitchProfileStatus.Failed);
                }
            }

            await _authenticationSession.PrepareForProfileAsync(
                targetCredential,
                cancellationToken);

            appliedCredential =
                await _authenticationSession.ReadCurrentCredentialAsync(
                    cancellationToken);
            if (!CredentialsEqual(
                    targetCredential,
                    appliedCredential))
            {
                return await RestoreAfterFailureAsync(
                    SwitchProfileStatus.AuthenticationMismatch,
                    cancellationToken);
            }

            var launchStatus = await _codexController.LaunchAsync(
                cancellationToken);
            if (launchStatus != CodexLaunchStatus.Launched)
            {
                return await RestoreAfterFailureAsync(
                    launchStatus == CodexLaunchStatus.InstallationNotFound
                        ? SwitchProfileStatus.InstallationNotFound
                        : SwitchProfileStatus.LaunchFailed,
                    cancellationToken);
            }

            if (!await _codexController.WaitForRunningAsync(
                    cancellationToken))
            {
                return await RestoreAfterFailureAsync(
                    SwitchProfileStatus.LaunchFailed,
                    cancellationToken);
            }

            processConfirmedRunning = true;
            CryptographicOperations.ZeroMemory(appliedCredential);
            appliedCredential =
                await _authenticationSession.ReadCurrentCredentialAsync(
                    cancellationToken);
            if (!CredentialsEqual(
                    targetCredential,
                    appliedCredential))
            {
                return await StopAndRestoreAfterMismatchAsync(
                    cancellationToken);
            }

            await _authenticationSession.ClearRecoveryAsync(
                cancellationToken);
            return Result(SwitchProfileStatus.Switched);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            if (processConfirmedRunning)
            {
                return Result(SwitchProfileStatus.RecoveryRequired);
            }

            return await RestoreAfterFailureAsync(
                SwitchProfileStatus.Failed,
                CancellationToken.None);
        }
        finally
        {
            if (targetCredential is not null)
            {
                CryptographicOperations.ZeroMemory(targetCredential);
            }

            if (currentCredential is not null)
            {
                CryptographicOperations.ZeroMemory(currentCredential);
            }

            if (appliedCredential is not null)
            {
                CryptographicOperations.ZeroMemory(appliedCredential);
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

    private async Task<SwitchProfileResult> StopAndRestoreAfterMismatchAsync(
        CancellationToken cancellationToken)
    {
        return await _codexController.ForceStopAsync(cancellationToken)
            ? await RestoreAfterFailureAsync(
                SwitchProfileStatus.AuthenticationMismatch,
                cancellationToken)
            : Result(SwitchProfileStatus.RecoveryRequired);
    }

    private async Task<SwitchProfileResult> RestoreAfterFailureAsync(
        SwitchProfileStatus failureStatus,
        CancellationToken cancellationToken)
    {
        if (!await _authenticationSession.HasPendingRecoveryAsync(
                cancellationToken))
        {
            return Result(failureStatus);
        }

        try
        {
            await _authenticationSession.RestorePreviousAsync(
                cancellationToken);
            await _authenticationSession.ClearRecoveryAsync(
                cancellationToken);
            return Result(failureStatus);
        }
        catch
        {
            return Result(SwitchProfileStatus.RecoveryRequired);
        }
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

    private static SwitchProfileResult Result(
        SwitchProfileStatus status) => new(status);
}
