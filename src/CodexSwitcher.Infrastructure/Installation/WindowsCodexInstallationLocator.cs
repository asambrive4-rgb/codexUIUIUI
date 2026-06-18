using System.Diagnostics;
using System.Text;
using CodexSwitcher.Core.Installation;

namespace CodexSwitcher.Infrastructure.Installation;

public sealed class WindowsCodexInstallationLocator : ICodexInstallationLocator
{
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(5);

    private const string ProbeScript =
        """
        $ErrorActionPreference = 'Stop'
        $package = Get-AppxPackage -Name 'OpenAI.Codex' |
            Sort-Object Version -Descending |
            Select-Object -First 1

        if ($null -eq $package) {
            exit 0
        }

        $app = Get-StartApps |
            Where-Object {
                $_.AppID -like ($package.PackageFamilyName + '!*')
            } |
            Select-Object -First 1

        if (
            $null -ne $app -and
            $app.AppID -like 'OpenAI.Codex_*!App'
        ) {
            [Console]::Out.Write($app.AppID)
        }
        """;

    public async Task<bool> IsInstalledAsync(CancellationToken cancellationToken)
    {
        return await FindAppUserModelIdAsync(cancellationToken) is not null;
    }

    public async Task<string?> FindAppUserModelIdAsync(
        CancellationToken cancellationToken)
    {
        var powershellPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.System),
            "WindowsPowerShell",
            "v1.0",
            "powershell.exe");

        if (!File.Exists(powershellPath))
        {
            return null;
        }

        var encodedCommand = Convert.ToBase64String(
            Encoding.Unicode.GetBytes(ProbeScript));

        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = powershellPath,
                Arguments = $"-NoLogo -NoProfile -NonInteractive -EncodedCommand {encodedCommand}",
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            }
        };

        if (!process.Start())
        {
            throw new InvalidOperationException(
                "Codex 설치 확인 프로세스를 시작하지 못했습니다.");
        }

        using var timeout = new CancellationTokenSource(ProbeTimeout);
        using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            timeout.Token);

        try
        {
            var standardOutput = process.StandardOutput.ReadToEndAsync(
                linkedCancellation.Token);
            var standardError = process.StandardError.ReadToEndAsync(
                linkedCancellation.Token);

            await process.WaitForExitAsync(linkedCancellation.Token);
            var output = await standardOutput;
            _ = await standardError;

            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException(
                    "Codex 설치 확인 프로세스가 실패했습니다.");
            }

            var appUserModelId = output.Trim();
            return appUserModelId.Length == 0
                ? null
                : appUserModelId;
        }
        catch (OperationCanceledException) when (
            timeout.IsCancellationRequested &&
            !cancellationToken.IsCancellationRequested)
        {
            TryKill(process);
            throw new TimeoutException("Codex 설치 확인 시간이 초과됐습니다.");
        }
        catch
        {
            TryKill(process);
            throw;
        }
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
            // 종료와 확인이 겹친 경우다.
        }
    }
}
