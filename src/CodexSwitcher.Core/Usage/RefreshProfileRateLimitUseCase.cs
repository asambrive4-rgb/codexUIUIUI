using System.Security.Cryptography;
using CodexSwitcher.Core.Profiles;

namespace CodexSwitcher.Core.Usage;

public sealed class RefreshProfileRateLimitUseCase
{
    internal const long FiveHourWindowMinutes = 5 * 60;
    internal const long WeeklyWindowMinutes = 7 * 24 * 60;

    private readonly IProfileStore _profileStore;
    private readonly IProfileRateLimitReader _reader;
    private readonly ProfileOperationCoordinator _operationCoordinator;

    public RefreshProfileRateLimitUseCase(
        IProfileStore profileStore,
        IProfileRateLimitReader reader,
        ProfileOperationCoordinator operationCoordinator)
    {
        _profileStore = profileStore;
        _reader = reader;
        _operationCoordinator = operationCoordinator;
    }

    public async Task<ProfileRateLimitSnapshot> ExecuteAsync(
        ProfileId profileId,
        bool keepAlive,
        CancellationToken cancellationToken)
    {
        using var operation =
            await _operationCoordinator.TryEnterAsync(cancellationToken);
        if (operation is null)
        {
            return Failed(profileId);
        }

        byte[]? credential = null;
        byte[]? refreshedCredential = null;

        try
        {
            credential = await _profileStore.ReadCredentialAsync(
                profileId,
                cancellationToken);
            var result = await _reader.ReadAsync(
                profileId,
                credential,
                keepAlive,
                cancellationToken);
            refreshedCredential = result.RefreshedCredential;

            if (result.Status == ProfileRateLimitStatus.Available &&
                refreshedCredential is not null &&
                !CredentialsEqual(credential, refreshedCredential))
            {
                await _profileStore.ReplaceCredentialAsync(
                    profileId,
                    refreshedCredential,
                    cancellationToken);
            }

            var fiveHour = result.Windows.FirstOrDefault(
                window =>
                    window.WindowDurationMinutes ==
                    FiveHourWindowMinutes);
            var weekly = result.Windows.FirstOrDefault(
                window =>
                    window.WindowDurationMinutes ==
                    WeeklyWindowMinutes);

            return new ProfileRateLimitSnapshot(
                profileId,
                result.Status,
                fiveHour,
                weekly,
                result.Status == ProfileRateLimitStatus.Available
                    ? DateTimeOffset.Now
                    : null);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (NotSupportedException)
        {
            return new ProfileRateLimitSnapshot(
                profileId,
                ProfileRateLimitStatus.UnsupportedAuthentication);
        }
        catch (DirectoryNotFoundException)
        {
            return Failed(profileId);
        }
        catch (FileNotFoundException)
        {
            return Failed(profileId);
        }
        catch
        {
            return Failed(profileId);
        }
        finally
        {
            if (credential is not null)
            {
                CryptographicOperations.ZeroMemory(credential);
            }

            if (refreshedCredential is not null)
            {
                CryptographicOperations.ZeroMemory(refreshedCredential);
            }
        }
    }

    private static bool CredentialsEqual(
        byte[] expected,
        byte[] actual)
    {
        return expected.Length == actual.Length &&
               CryptographicOperations.FixedTimeEquals(
                   expected,
                   actual);
    }

    private static ProfileRateLimitSnapshot Failed(ProfileId profileId) =>
        new(profileId, ProfileRateLimitStatus.Failed);
}
