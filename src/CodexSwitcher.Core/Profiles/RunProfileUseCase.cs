using System.Security.Cryptography;

namespace CodexSwitcher.Core.Profiles;

public sealed class RunProfileUseCase
{
    private readonly IProfileStore _profileStore;
    private readonly IAuthenticationSession _authenticationSession;
    private readonly ICodexLoginController _codexController;
    private readonly ProfileOperationCoordinator _operationCoordinator;

    public RunProfileUseCase(
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

    public async Task<RunProfileResult> ExecuteAsync(
        ProfileId profileId,
        CancellationToken cancellationToken)
    {
        using var operation =
            await _operationCoordinator.TryEnterAsync(cancellationToken);
        if (operation is null)
        {
            return Result(RunProfileStatus.Failed);
        }

        byte[]? targetCredential = null;
        byte[]? appliedCredential = null;
        var processConfirmedRunning = false;

        try
        {
            if (await _authenticationSession.HasPendingRecoveryAsync(
                    cancellationToken))
            {
                return Result(RunProfileStatus.RecoveryRequired);
            }

            if (await _codexController.IsRunningAsync(cancellationToken))
            {
                return Result(RunProfileStatus.AlreadyRunning);
            }

            var profiles = await _profileStore.ReadAllAsync(
                cancellationToken);
            if (profiles.Profiles.All(profile => profile.Id != profileId))
            {
                return Result(RunProfileStatus.ProfileNotFound);
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
                return Result(RunProfileStatus.ProfileNotFound);
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
                    RunProfileStatus.AuthenticationMismatch,
                    cancellationToken);
            }

            var launchStatus = await _codexController.LaunchAsync(
                cancellationToken);
            if (launchStatus != CodexLaunchStatus.Launched)
            {
                return await RestoreAfterFailureAsync(
                    launchStatus == CodexLaunchStatus.InstallationNotFound
                        ? RunProfileStatus.InstallationNotFound
                        : RunProfileStatus.LaunchFailed,
                    cancellationToken);
            }

            if (!await _codexController.WaitForRunningAsync(
                    cancellationToken))
            {
                return await RestoreAfterFailureAsync(
                    RunProfileStatus.LaunchFailed,
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
            return Result(RunProfileStatus.Running);
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
                return Result(RunProfileStatus.RecoveryRequired);
            }

            return await RestoreAfterFailureAsync(
                RunProfileStatus.Failed,
                CancellationToken.None);
        }
        finally
        {
            if (targetCredential is not null)
            {
                CryptographicOperations.ZeroMemory(targetCredential);
            }

            if (appliedCredential is not null)
            {
                CryptographicOperations.ZeroMemory(appliedCredential);
            }
        }
    }

    private async Task<RunProfileResult> StopAndRestoreAfterMismatchAsync(
        CancellationToken cancellationToken)
    {
        var stopStatus = await _codexController.RequestStopAsync(
            cancellationToken);
        return stopStatus == CodexStopStatus.Stopped
            ? await RestoreAfterFailureAsync(
                RunProfileStatus.AuthenticationMismatch,
                cancellationToken)
            : Result(RunProfileStatus.RecoveryRequired);
    }

    private async Task<RunProfileResult> RestoreAfterFailureAsync(
        RunProfileStatus failureStatus,
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
            return Result(RunProfileStatus.RecoveryRequired);
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

    private static RunProfileResult Result(RunProfileStatus status) =>
        new(status);
}
