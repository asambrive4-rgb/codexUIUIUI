using System.Diagnostics;

namespace CodexSwitcher.Feasibility;

internal sealed class CodexLauncher
{
    public void Launch(CodexInstallation installation)
    {
        if (installation.IsSupportedStoreInstallation)
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = $"shell:AppsFolder\\{installation.AppUserModelId}",
                UseShellExecute = true
            });
            return;
        }

        if (installation.Kind == CodexInstallKind.Standalone &&
            !string.IsNullOrWhiteSpace(installation.ExecutablePath))
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = installation.ExecutablePath,
                UseShellExecute = true
            });
            return;
        }

        throw new InvalidOperationException("실행할 수 있는 Codex 설치를 찾지 못했습니다.");
    }
}

