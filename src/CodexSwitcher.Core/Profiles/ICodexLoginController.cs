namespace CodexSwitcher.Core.Profiles;

public interface ICodexLoginController
{
    Task<bool> IsRunningAsync(
        CancellationToken cancellationToken);

    Task<CodexStopStatus> RequestStopAsync(
        CancellationToken cancellationToken);

    Task<bool> ForceStopAsync(
        CancellationToken cancellationToken);

    Task<CodexLaunchStatus> LaunchAsync(
        CancellationToken cancellationToken);

    Task<bool> WaitForRunningAsync(
        CancellationToken cancellationToken);
}
