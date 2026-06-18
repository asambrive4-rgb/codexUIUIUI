using System.ComponentModel;
using System.Runtime.InteropServices;

namespace CodexSwitcher.Feasibility;

internal sealed record RestartManagerResult(bool RequestAccepted, int ErrorCode);

internal sealed class RestartManagerShutdown
{
    public RestartManagerResult RequestShutdown(IReadOnlyList<int> processIds)
    {
        if (processIds.Count == 0)
        {
            return new RestartManagerResult(true, 0);
        }

        var startResult = RmStartSession(
            out var sessionHandle,
            0,
            Guid.NewGuid().ToString("N"));

        if (startResult != 0)
        {
            return new RestartManagerResult(false, startResult);
        }

        try
        {
            var resources = processIds
                .Select(TryCreateUniqueProcess)
                .Where(process => process.HasValue)
                .Select(process => process.GetValueOrDefault())
                .ToArray();

            if (resources.Length == 0)
            {
                return new RestartManagerResult(false, 5);
            }

            var registerResult = RmRegisterResources(
                sessionHandle,
                0,
                null,
                (uint)resources.Length,
                resources,
                0,
                null);

            if (registerResult != 0)
            {
                return new RestartManagerResult(false, registerResult);
            }

            var shutdownResult = RmShutdown(sessionHandle, 0, null);
            return new RestartManagerResult(shutdownResult == 0, shutdownResult);
        }
        finally
        {
            _ = RmEndSession(sessionHandle);
        }
    }

    public static string DescribeError(int errorCode)
    {
        return errorCode == 0 ? "없음" : new Win32Exception(errorCode).Message;
    }

    private static RmUniqueProcess? TryCreateUniqueProcess(int processId)
    {
        try
        {
            using var process = System.Diagnostics.Process.GetProcessById(processId);
            var startTime = process.StartTime.ToFileTime();
            return new RmUniqueProcess
            {
                ProcessId = processId,
                ProcessStartTime = new NativeFileTime
                {
                    LowDateTime = unchecked((uint)startTime),
                    HighDateTime = unchecked((uint)(startTime >> 32))
                }
            };
        }
        catch (Exception exception) when (
            exception is ArgumentException or
            InvalidOperationException or
            Win32Exception)
        {
            return null;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeFileTime
    {
        public uint LowDateTime;
        public uint HighDateTime;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RmUniqueProcess
    {
        public int ProcessId;
        public NativeFileTime ProcessStartTime;
    }

    private delegate void RmWriteStatusCallback(uint percentComplete);

    [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)]
    private static extern int RmStartSession(
        out uint sessionHandle,
        int sessionFlags,
        string sessionKey);

    [DllImport("rstrtmgr.dll")]
    private static extern int RmRegisterResources(
        uint sessionHandle,
        uint fileCount,
        string[]? fileNames,
        uint applicationCount,
        RmUniqueProcess[] applications,
        uint serviceCount,
        string[]? serviceNames);

    [DllImport("rstrtmgr.dll")]
    private static extern int RmShutdown(
        uint sessionHandle,
        uint flags,
        RmWriteStatusCallback? statusCallback);

    [DllImport("rstrtmgr.dll")]
    private static extern int RmEndSession(uint sessionHandle);
}
