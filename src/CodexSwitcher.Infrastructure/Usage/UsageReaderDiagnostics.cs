namespace CodexSwitcher.Infrastructure.Usage;

internal static class UsageReaderDiagnostics
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
                File.AppendAllText(
                    path,
                    $"{DateTimeOffset.Now:O} reader {message}{Environment.NewLine}");
            }
        }
        catch
        {
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
