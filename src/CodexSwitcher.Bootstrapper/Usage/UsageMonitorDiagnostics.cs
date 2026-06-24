using System.IO;

namespace CodexSwitcher.Bootstrapper.Usage;

internal static class UsageMonitorDiagnostics
{
    private static readonly object Gate = new();

    public static void Write(string message)
    {
        try
        {
            var root = Path.Combine(
                Environment.GetFolderPath(
                    Environment.SpecialFolder.LocalApplicationData),
                "CodexAccountSwitcher");
            Directory.CreateDirectory(root);

            var path = Path.Combine(root, "usage-monitor.log");
            lock (Gate)
            {
                if (File.Exists(path) &&
                    new FileInfo(path).Length > 1_000_000)
                {
                    File.WriteAllText(path, "");
                }

                File.AppendAllText(
                    path,
                    $"{DateTimeOffset.Now:O} {message}{Environment.NewLine}");
            }
        }
        catch
        {
            // Diagnostics must never affect usage monitoring.
        }
    }
}
