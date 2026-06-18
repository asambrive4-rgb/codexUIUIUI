using System.Security.Cryptography;

namespace CodexSwitcher.Core.Profiles;

public sealed class CreateProfileLoginUseCase
{
    private readonly IProfileStore _profileStore;
    private readonly ProfileCreationValidator _validator;
    private readonly IAuthenticationSession _authenticationSession;
    private readonly ICodexLoginController _codexController;
    private readonly ProfileOperationCoordinator _operationCoordinator;
    private ProfileName? _pendingProfileName;

    public CreateProfileLoginUseCase(
        IProfileStore profileStore,
        IAuthenticationSession authenticationSession,
        ICodexLoginController codexController,
        ProfileOperationCoordinator? operationCoordinator = null)
    {
        _profileStore = profileStore;
        _validator = new ProfileCreationValidator(profileStore);
        _authenticationSession = authenticationSession;
        _codexController = codexController;
        _operationCoordinator =
            operationCoordinator ?? new ProfileOperationCoordinator();
    }

    public Task<bool> HasPendingRecoveryAsync(
        CancellationToken cancellationToken)
    {
        return _authenticationSession.HasPendingRecoveryAsync(
            cancellationToken);
    }

    public async Task<CreateProfileLoginResult> StartAsync(
        string name,
        bool forceCloseApproved,
        CancellationToken cancellationToken)
    {
        using var operation =
            await _operationCoordinator.TryEnterAsync(cancellationToken);
        if (operation is null)
        {
            return Failed();
        }

        try
        {
            if (_pendingProfileName is not null ||
                await _authenticationSession.HasPendingRecoveryAsync(
                    cancellationToken))
            {
                return Result(CreateProfileLoginStatus.RecoveryRequired);
            }

            var validation = await _validator.ValidateAsync(
                name,
                cancellationToken);
            var invalidResult = MapValidation(validation.Status);
            if (invalidResult is not null)
            {
                return invalidResult;
            }

            var stopResult = await EnsureStoppedAsync(
                forceCloseApproved,
                cancellationToken);
            if (stopResult is not null)
            {
                return stopResult;
            }

            await _authenticationSession.PrepareForLoginAsync(
                cancellationToken);

            var launchStatus = await _codexController.LaunchAsync(
                cancellationToken);
            if (launchStatus != CodexLaunchStatus.Launched)
            {
                return await RestoreAfterFailureAsync(
                    launchStatus == CodexLaunchStatus.InstallationNotFound
                        ? CreateProfileLoginStatus.InstallationNotFound
                        : CreateProfileLoginStatus.Failed,
                    cancellationToken);
            }

            _pendingProfileName = validation.Name;
            return Result(CreateProfileLoginStatus.WaitingForLogin);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return await RestoreAfterFailureAsync(
                CreateProfileLoginStatus.Failed,
                CancellationToken.None);
        }
    }

    public async Task<CreateProfileLoginResult> CompleteAsync(
        bool forceCloseApproved,
        CancellationToken cancellationToken)
    {
        using var operation =
            await _operationCoordinator.TryEnterAsync(cancellationToken);
        if (operation is null)
        {
            return Failed();
        }

        byte[]? credential = null;

        try
        {
            if (_pendingProfileName is null)
            {
                return await _authenticationSession.HasPendingRecoveryAsync(
                        cancellationToken)
                    ? Result(CreateProfileLoginStatus.RecoveryRequired)
                    : Failed();
            }

            var stopResult = await EnsureStoppedAsync(
                forceCloseApproved,
                cancellationToken);
            if (stopResult is not null)
            {
                return stopResult;
            }

            credential =
                await _authenticationSession.ReadCurrentCredentialAsync(
                    cancellationToken);
            if (credential is null)
            {
                return await RestoreAndFinishAsync(
                    CreateProfileLoginStatus.LoginNotCompleted,
                    cancellationToken);
            }

            await _authenticationSession.RestorePreviousAsync(
                cancellationToken);
            await _authenticationSession.ClearRecoveryAsync(
                cancellationToken);

            var profile = new Profile(
                ProfileId.New(),
                _pendingProfileName);
            await _profileStore.SaveAsync(
                profile,
                credential,
                cancellationToken);
            _pendingProfileName = null;

            return new CreateProfileLoginResult(
                CreateProfileLoginStatus.Created,
                profile);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return await RestoreAfterFailureAsync(
                CreateProfileLoginStatus.Failed,
                CancellationToken.None);
        }
        finally
        {
            if (credential is not null)
            {
                CryptographicOperations.ZeroMemory(credential);
            }
        }
    }

    public async Task<CreateProfileLoginResult> CancelAsync(
        bool forceCloseApproved,
        CancellationToken cancellationToken)
    {
        using var operation =
            await _operationCoordinator.TryEnterAsync(cancellationToken);
        if (operation is null)
        {
            return Failed();
        }

        try
        {
            if (!await _authenticationSession.HasPendingRecoveryAsync(
                    cancellationToken))
            {
                _pendingProfileName = null;
                return Result(CreateProfileLoginStatus.Canceled);
            }

            var stopResult = await EnsureStoppedAsync(
                forceCloseApproved,
                cancellationToken);
            if (stopResult is not null)
            {
                return stopResult;
            }

            return await RestoreAndFinishAsync(
                CreateProfileLoginStatus.Canceled,
                cancellationToken);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return Result(CreateProfileLoginStatus.RecoveryRequired);
        }
    }

    private async Task<CreateProfileLoginResult?> EnsureStoppedAsync(
        bool forceCloseApproved,
        CancellationToken cancellationToken)
    {
        var status = await _codexController.RequestStopAsync(
            cancellationToken);
        if (status == CodexStopStatus.Stopped)
        {
            return null;
        }

        if (status == CodexStopStatus.Failed)
        {
            return Failed();
        }

        if (!forceCloseApproved)
        {
            return Result(
                CreateProfileLoginStatus.ForceCloseConfirmationRequired);
        }

        return await _codexController.ForceStopAsync(cancellationToken)
            ? null
            : Failed();
    }

    private async Task<CreateProfileLoginResult> RestoreAndFinishAsync(
        CreateProfileLoginStatus finalStatus,
        CancellationToken cancellationToken)
    {
        try
        {
            await _authenticationSession.RestorePreviousAsync(
                cancellationToken);
            await _authenticationSession.ClearRecoveryAsync(
                cancellationToken);
            _pendingProfileName = null;
            return Result(finalStatus);
        }
        catch
        {
            return Result(CreateProfileLoginStatus.RecoveryRequired);
        }
    }

    private async Task<CreateProfileLoginResult> RestoreAfterFailureAsync(
        CreateProfileLoginStatus finalStatus,
        CancellationToken cancellationToken)
    {
        if (!await _authenticationSession.HasPendingRecoveryAsync(
                cancellationToken))
        {
            _pendingProfileName = null;
            return Result(finalStatus);
        }

        var restored = await RestoreAndFinishAsync(
            finalStatus,
            cancellationToken);
        return restored.Status == CreateProfileLoginStatus.RecoveryRequired
            ? restored
            : Result(finalStatus);
    }

    private static CreateProfileLoginResult? MapValidation(
        ProfileCreationValidationStatus status)
    {
        return status switch
        {
            ProfileCreationValidationStatus.Valid => null,
            ProfileCreationValidationStatus.InvalidName =>
                Result(CreateProfileLoginStatus.InvalidName),
            ProfileCreationValidationStatus.DuplicateName =>
                Result(CreateProfileLoginStatus.DuplicateName),
            _ => Result(CreateProfileLoginStatus.StorageNeedsAttention)
        };
    }

    private static CreateProfileLoginResult Result(
        CreateProfileLoginStatus status) =>
        new(status);

    private static CreateProfileLoginResult Failed() =>
        Result(CreateProfileLoginStatus.Failed);
}
