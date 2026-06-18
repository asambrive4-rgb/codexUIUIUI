using System.Diagnostics;
using System.Runtime.InteropServices;

namespace CodexSwitcher.Feasibility;

internal sealed record CodexProcessStatus(int ProcessCount, bool HasCloseableWindow)
{
    public bool IsRunning => ProcessCount > 0;
}

internal sealed record CloseRequestResult(
    bool WasRunning,
    bool CloseRequested,
    bool ExitedWithinTimeout,
    int RemainingProcessCount,
    string Strategy,
    int? NativeErrorCode);

internal sealed class CodexProcessController
{
    private readonly ProcessTreeInspector _processTreeInspector;
    private readonly RestartManagerShutdown _restartManagerShutdown;

    public CodexProcessController(
        ProcessTreeInspector processTreeInspector,
        RestartManagerShutdown restartManagerShutdown)
    {
        _processTreeInspector = processTreeInspector;
        _restartManagerShutdown = restartManagerShutdown;
    }

    public CodexProcessStatus Inspect()
    {
        using var processes = new ProcessCollection(GetCodexProcesses());
        var processIds = processes.Items.Select(process => (uint)process.Id).ToHashSet();
        return new CodexProcessStatus(
            processes.Items.Count,
            FindTopLevelWindows(processIds).Count > 0);
    }

    public async Task<CloseRequestResult> RequestNormalCloseAsync(TimeSpan timeout)
    {
        var treeStatus = _processTreeInspector.InspectCurrentProcess();
        if (treeStatus.IsDescendantOfCodex)
        {
            throw new InvalidOperationException(
                "Codex 내부 터미널에서는 Codex를 안전하게 종료할 수 없습니다. Windows Terminal 또는 시작 메뉴에서 연 외부 PowerShell에서 다시 실행하세요.");
        }

        using var processes = new ProcessCollection(GetCodexProcesses());
        if (processes.Items.Count == 0)
        {
            return new CloseRequestResult(false, false, true, 0, "없음", null);
        }

        var processIds = processes.Items.Select(process => (uint)process.Id).ToHashSet();
        var closeRequested = false;
        foreach (var window in FindTopLevelWindows(processIds))
        {
            if (PostMessage(window, WindowMessageClose, IntPtr.Zero, IntPtr.Zero))
            {
                closeRequested = true;
            }
        }

        if (closeRequested && await WaitForExitAsync(TimeSpan.FromSeconds(2)))
        {
            return new CloseRequestResult(true, true, true, 0, "WM_CLOSE", null);
        }

        var remaining = GetCodexProcesses();
        RestartManagerResult restartManagerResult;
        try
        {
            restartManagerResult = _restartManagerShutdown.RequestShutdown(
                remaining.Select(process => process.Id).ToArray());
        }
        finally
        {
            foreach (var process in remaining)
            {
                process.Dispose();
            }
        }

        if (restartManagerResult.RequestAccepted && await WaitForExitAsync(timeout))
        {
            return new CloseRequestResult(
                true,
                true,
                true,
                0,
                "Windows Restart Manager",
                null);
        }

        var remainingCount = GetCodexProcessCount();
        return new CloseRequestResult(
            true,
            closeRequested || restartManagerResult.RequestAccepted,
            remainingCount == 0,
            remainingCount,
            restartManagerResult.RequestAccepted ? "Windows Restart Manager" : "WM_CLOSE",
            restartManagerResult.ErrorCode == 0 ? null : restartManagerResult.ErrorCode);
    }

    public int ForceStop()
    {
        var treeStatus = _processTreeInspector.InspectCurrentProcess();
        if (treeStatus.IsDescendantOfCodex)
        {
            throw new InvalidOperationException(
                "Codex 내부 터미널에서는 강제 종료할 수 없습니다. 외부 PowerShell에서 실행하세요.");
        }

        using var processes = new ProcessCollection(GetCodexProcesses());
        foreach (var process in processes.Items.OrderByDescending(process => process.Id))
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException)
            {
                // 다른 프로세스를 종료하는 동안 이미 끝난 프로세스다.
            }
            catch (System.ComponentModel.Win32Exception)
            {
                // 남은 프로세스 수로 실패를 보고한다.
            }
        }

        var deadline = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(10);
        while (DateTimeOffset.UtcNow < deadline)
        {
            var remaining = GetCodexProcessCount();
            if (remaining == 0)
            {
                return 0;
            }

            Thread.Sleep(250);
        }

        return GetCodexProcessCount();
    }

    private static int GetCodexProcessCount()
    {
        var processes = GetCodexProcesses();
        try
        {
            return processes.Count;
        }
        finally
        {
            foreach (var process in processes)
            {
                process.Dispose();
            }
        }
    }

    private static async Task<bool> WaitForExitAsync(TimeSpan timeout)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (GetCodexProcessCount() == 0)
            {
                return true;
            }

            await Task.Delay(250);
        }

        return GetCodexProcessCount() == 0;
    }

    public void EnsureStopped()
    {
        var status = Inspect();
        if (status.IsRunning)
        {
            throw new CodexRunningException(
                $"Codex 관련 프로세스 {status.ProcessCount}개가 실행 중입니다. 인증 파일을 바꾸지 않았습니다.");
        }
    }

    private static List<Process> GetCodexProcesses()
    {
        return Process
            .GetProcesses()
            .Where(process =>
                string.Equals(process.ProcessName, "Codex", StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    private static List<IntPtr> FindTopLevelWindows(HashSet<uint> processIds)
    {
        var windows = new List<IntPtr>();
        _ = EnumWindows(
            (window, _) =>
            {
                GetWindowThreadProcessId(window, out var processId);
                if (processIds.Contains(processId))
                {
                    windows.Add(window);
                }

                return true;
            },
            IntPtr.Zero);

        return windows;
    }

    private const uint WindowMessageClose = 0x0010;

    private delegate bool EnumWindowsCallback(IntPtr window, IntPtr parameter);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumWindows(EnumWindowsCallback callback, IntPtr parameter);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostMessage(
        IntPtr window,
        uint message,
        IntPtr wordParameter,
        IntPtr longParameter);

    private sealed class ProcessCollection : IDisposable
    {
        public ProcessCollection(List<Process> items)
        {
            Items = items;
        }

        public List<Process> Items { get; }

        public void Dispose()
        {
            foreach (var process in Items)
            {
                process.Dispose();
            }
        }
    }
}

internal sealed class CodexRunningException : InvalidOperationException
{
    public CodexRunningException(string message)
        : base(message)
    {
    }
}
