namespace CodexSwitcher.Core.Installation;

public interface ICodexInstallationLocator
{
    Task<bool> IsInstalledAsync(CancellationToken cancellationToken);
}

