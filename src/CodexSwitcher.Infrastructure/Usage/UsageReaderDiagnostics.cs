namespace CodexSwitcher.Infrastructure.Usage;

internal static class UsageReaderDiagnostics
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
                File.AppendAllText(
                    path,
                    $"{DateTimeOffset.Now:O} reader {message}{Environment.NewLine}");
            }
        }
        catch
        {
        }
    }
}
