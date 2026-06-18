using System.Diagnostics;
using CodexSwitcher.Core.Profiles;
using CodexSwitcher.Infrastructure.Installation;

namespace CodexSwitcher.Infrastructure.Profiles;

public sealed class WindowsCodexLoginController : ICodexLoginController
{
    private static readonly TimeSpan NormalStopTimeout =
        TimeSpan.FromSeconds(10);

    private readonly WindowsCodexInstallationLocator _installationLocator;

    public WindowsCodexLoginController(
        WindowsCodexInstallationLocator installationLocator)
    {
        _installationLocator = installationLocator;
    }

    public Task<bool> IsRunningAsync(
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var processes = GetCodexProcesses();
        try
        {
            return Task.FromResult(processes.Count > 0);
        }
        finally
        {
            DisposeAll(processes);
        }
    }

    public async Task<CodexStopStatus> RequestStopAsync(
        CancellationToken cancellationToken)
    {
        var processes = GetCodexProcesses();

        try
        {
            if (processes.Count == 0)
            {
                return CodexStopStatus.Stopped;
            }

            foreach (var process in processes)
            {
                try
                {
                    _ = process.CloseMainWindow();
                }
                catch (InvalidOperationException)
                {
                    // 확인 중 이미 종료된 프로세스다.
                }
            }

            var stopped = await WaitForStopAsync(
                NormalStopTimeout,
                cancellationToken);
            return stopped
                ? CodexStopStatus.Stopped
                : CodexStopStatus.ForceCloseRequired;
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return CodexStopStatus.Failed;
        }
        finally
        {
            DisposeAll(processes);
        }
    }

    public async Task<bool> ForceStopAsync(
        CancellationToken cancellationToken)
    {
        // 임시방편: 현재 Store형 Codex가 정상 종료 요청에 응답하지 않는
        // 경우가 있어 사용자 동의 후에만 강제 종료한다. 추후 신뢰할 수
        // 있는 정상 종료 방식을 찾으면 이 분기를 교체해야 한다.
        var processes = GetCodexProcesses();

        try
        {
            foreach (var process in processes)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch (InvalidOperationException)
                {
                    // 다른 프로세스를 종료하는 동안 이미 끝났다.
                }
                catch (System.ComponentModel.Win32Exception)
                {
                    return false;
                }
            }

            return await WaitForStopAsync(
                TimeSpan.FromSeconds(10),
                cancellationToken);
        }
        finally
        {
            DisposeAll(processes);
        }
    }

    public async Task<CodexLaunchStatus> LaunchAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            var appUserModelId =
                await _installationLocator.FindAppUserModelIdAsync(
                    cancellationToken);
            if (appUserModelId is null)
            {
                return CodexLaunchStatus.InstallationNotFound;
            }

            _ = Process.Start(
                new ProcessStartInfo
                {
                    FileName =
                        $"shell:AppsFolder\\{appUserModelId}",
                    UseShellExecute = true
                });
            return CodexLaunchStatus.Launched;
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return CodexLaunchStatus.Failed;
        }
    }

    public Task<bool> WaitForRunningAsync(
        CancellationToken cancellationToken)
    {
        return WaitForRunningCoreAsync(
            TimeSpan.FromSeconds(10),
            cancellationToken);
    }

    private static async Task<bool> WaitForRunningCoreAsync(
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var processes = GetCodexProcesses();
            var running = processes.Count > 0;
            DisposeAll(processes);

            if (running)
            {
                return true;
            }

            await Task.Delay(250, cancellationToken);
        }

        var remaining = GetCodexProcesses();
        var result = remaining.Count > 0;
        DisposeAll(remaining);
        return result;
    }

    private static async Task<bool> WaitForStopAsync(
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var processes = GetCodexProcesses();
            var stopped = processes.Count == 0;
            DisposeAll(processes);

            if (stopped)
            {
                return true;
            }

            await Task.Delay(250, cancellationToken);
        }

        var remaining = GetCodexProcesses();
        var result = remaining.Count == 0;
        DisposeAll(remaining);
        return result;
    }

    private static List<Process> GetCodexProcesses()
    {
        return Process
            .GetProcessesByName("Codex")
            .ToList();
    }

    private static void DisposeAll(IEnumerable<Process> processes)
    {
        foreach (var process in processes)
        {
            process.Dispose();
        }
    }
}
