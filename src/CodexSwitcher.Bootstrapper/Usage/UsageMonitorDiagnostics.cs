using System.IO;

namespace CodexSwitcher.Bootstrapper.Usage;

internal static class UsageMonitorDiagnostics
{
    private static readonly bool IsEnabled =
        IsDiagnosticsEnabled();
    private static readonly object Gate = new();

    public static void Write(string message)
    {
        if (!IsEnabled)
        {
            return;
        }

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

    private static bool IsDiagnosticsEnabled()
    {
        var value = Environment.GetEnvironmentVariable(
            "CODEX_SWITCHER_USAGE_DIAGNOSTICS");
        return string.Equals(value, "1", StringComparison.Ordinal) ||
               string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
    }
}
