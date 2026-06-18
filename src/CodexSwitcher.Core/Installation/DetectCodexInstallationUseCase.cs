namespace CodexSwitcher.Core.Installation;

public sealed class DetectCodexInstallationUseCase
{
    private readonly ICodexInstallationLocator _installationLocator;

    public DetectCodexInstallationUseCase(
        ICodexInstallationLocator installationLocator)
    {
        _installationLocator = installationLocator;
    }

    public async Task<DetectCodexInstallationResult> ExecuteAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            var isInstalled = await _installationLocator.IsInstalledAsync(cancellationToken);
            return new DetectCodexInstallationResult(
                isInstalled
                    ? CodexInstallationDetectionStatus.Installed
                    : CodexInstallationDetectionStatus.NotInstalled);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return new DetectCodexInstallationResult(
                CodexInstallationDetectionStatus.Failed);
        }
    }
}

