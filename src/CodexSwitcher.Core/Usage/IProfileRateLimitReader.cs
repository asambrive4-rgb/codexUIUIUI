using CodexSwitcher.Core.Profiles;

namespace CodexSwitcher.Core.Usage;

public interface IProfileRateLimitReader : IDisposable
{
    Task<ProfileRateLimitReadResult> ReadAsync(
        ProfileId profileId,
        ReadOnlyMemory<byte> credential,
        bool keepAlive,
        CancellationToken cancellationToken);

    void StopActiveSession();
}
