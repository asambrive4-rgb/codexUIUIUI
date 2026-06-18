using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace CodexSwitcher.Feasibility;

internal sealed record ProcessTreeStatus(
    bool IsDescendantOfCodex,
    int? CodexAncestorProcessId);

internal sealed class ProcessTreeInspector
{
    public ProcessTreeStatus InspectCurrentProcess()
    {
        var processes = CaptureProcesses();
        return Inspect(
            Environment.ProcessId,
            processes.ToDictionary(process => process.ProcessId));
    }

    internal static ProcessTreeStatus Inspect(
        int currentProcessId,
        IReadOnlyDictionary<int, ProcessTreeEntry> processes)
    {
        var visited = new HashSet<int>();
        var processId = currentProcessId;

        while (processes.TryGetValue(processId, out var process) &&
               process.ParentProcessId > 0 &&
               visited.Add(processId))
        {
            if (!processes.TryGetValue(process.ParentProcessId, out var parent))
            {
                break;
            }

            if (string.Equals(
                    Path.GetFileNameWithoutExtension(parent.ExecutableName),
                    "Codex",
                    StringComparison.OrdinalIgnoreCase))
            {
                return new ProcessTreeStatus(true, parent.ProcessId);
            }

            processId = parent.ProcessId;
        }

        return new ProcessTreeStatus(false, null);
    }

    private static List<ProcessTreeEntry> CaptureProcesses()
    {
        var snapshot = CreateToolhelp32Snapshot(SnapshotProcesses, 0);
        if (snapshot == InvalidHandleValue)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        try
        {
            var entry = new ProcessEntry32
            {
                Size = (uint)Marshal.SizeOf<ProcessEntry32>()
            };
            var result = new List<ProcessTreeEntry>();

            if (!Process32First(snapshot, ref entry))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }

            do
            {
                result.Add(new ProcessTreeEntry(
                    checked((int)entry.ProcessId),
                    checked((int)entry.ParentProcessId),
                    entry.ExecutableFile));
                entry.Size = (uint)Marshal.SizeOf<ProcessEntry32>();
            }
            while (Process32Next(snapshot, ref entry));

            return result;
        }
        finally
        {
            _ = CloseHandle(snapshot);
        }
    }

    private const uint SnapshotProcesses = 0x00000002;
    private static readonly IntPtr InvalidHandleValue = new(-1);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ProcessEntry32
    {
        public uint Size;
        public uint Usage;
        public uint ProcessId;
        public IntPtr DefaultHeapId;
        public uint ModuleId;
        public uint Threads;
        public uint ParentProcessId;
        public int PriorityClassBase;
        public uint Flags;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string ExecutableFile;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr CreateToolhelp32Snapshot(uint flags, uint processId);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool Process32First(IntPtr snapshot, ref ProcessEntry32 entry);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool Process32Next(IntPtr snapshot, ref ProcessEntry32 entry);

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr handle);
}

internal sealed record ProcessTreeEntry(
    int ProcessId,
    int ParentProcessId,
    string ExecutableName);

