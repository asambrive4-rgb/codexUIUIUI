using System.Security.Cryptography;

namespace CodexSwitcher.Core.Profiles;

public sealed class GetProfileRuntimeStateUseCase
{
    private readonly IProfileStore _profileStore;
    private readonly IAuthenticationSession _authenticationSession;
    private readonly ICodexLoginController _codexController;

    public GetProfileRuntimeStateUseCase(
        IProfileStore profileStore,
        IAuthenticationSession authenticationSession,
        ICodexLoginController codexController)
    {
        _profileStore = profileStore;
        _authenticationSession = authenticationSession;
        _codexController = codexController;
    }

    public async Task<ProfileRuntimeState> ExecuteAsync(
        CancellationToken cancellationToken)
    {
        if (!await _codexController.IsRunningAsync(cancellationToken))
        {
            return new ProfileRuntimeState(
                ProfileRuntimeStatus.Stopped);
        }

        byte[]? currentCredential = null;
        try
        {
            currentCredential =
                await _authenticationSession.ReadCurrentCredentialAsync(
                    cancellationToken);
            if (currentCredential is null)
            {
                return Unknown();
            }

            var stored = await _profileStore.ReadAllAsync(
                cancellationToken);
            var matches = new List<ProfileId>();

            foreach (var profile in stored.Profiles)
            {
                byte[]? storedCredential = null;
                try
                {
                    storedCredential =
                        await _profileStore.ReadCredentialAsync(
                            profile.Id,
                            cancellationToken);
                    if (storedCredential.Length ==
                        currentCredential.Length &&
                        CryptographicOperations.FixedTimeEquals(
                            storedCredential,
                            currentCredential))
                    {
                        matches.Add(profile.Id);
                    }
                }
                catch (Exception exception)
                    when (exception is IOException or
                          UnauthorizedAccessException or
                          InvalidDataException or
                          CryptographicException)
                {
                    // 읽을 수 없는 프로필은 활성 후보에서 제외한다.
                }
                finally
                {
                    if (storedCredential is not null)
                    {
                        CryptographicOperations.ZeroMemory(
                            storedCredential);
                    }
                }
            }

            return matches.Count == 1
                ? new ProfileRuntimeState(
                    ProfileRuntimeStatus.RunningKnownProfile,
                    matches[0])
                : Unknown();
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return Unknown();
        }
        finally
        {
            if (currentCredential is not null)
            {
                CryptographicOperations.ZeroMemory(
                    currentCredential);
            }
        }
    }

    private static ProfileRuntimeState Unknown() =>
        new(ProfileRuntimeStatus.RunningUnknownProfile);
}
