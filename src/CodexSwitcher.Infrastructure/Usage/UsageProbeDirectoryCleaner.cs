namespace CodexSwitcher.Infrastructure.Usage;

internal static class UsageProbeDirectoryCleaner
{
    public static bool TryDeleteSessionDirectory(
        string probeRoot,
        string sessionDirectory)
    {
        try
        {
            if (!Directory.Exists(sessionDirectory))
            {
                return true;
            }

            if (!IsDirectChild(probeRoot, sessionDirectory) ||
                IsReparsePoint(sessionDirectory))
            {
                return false;
            }

            var allEntriesDeleted = true;
            foreach (var entry in Directory.EnumerateFileSystemEntries(
                         sessionDirectory,
                         "*",
                         SearchOption.AllDirectories)
                     .OrderByDescending(entry => entry.Length))
            {
                allEntriesDeleted &= TryDeleteEntry(entry);
            }

            if (!allEntriesDeleted ||
                Directory.EnumerateFileSystemEntries(sessionDirectory).Any())
            {
                return false;
            }

            return TryDeleteEntry(sessionDirectory) &&
                   !Directory.Exists(sessionDirectory);
        }
        catch (Exception exception)
            when (IsRecoverableCleanupException(exception))
        {
            return false;
        }
    }

    internal static bool IsRecoverableCleanupException(
        Exception exception) =>
        exception is IOException or
            UnauthorizedAccessException or
            InvalidOperationException or
            System.ComponentModel.Win32Exception or
            ArgumentException or
            NotSupportedException;

    private static bool TryDeleteEntry(string path)
    {
        FileAttributes attributes;
        try
        {
            attributes = File.GetAttributes(path);
        }
        catch (Exception exception)
            when (IsRecoverableCleanupException(exception))
        {
            return !File.Exists(path) && !Directory.Exists(path);
        }

        if ((attributes & FileAttributes.ReparsePoint) != 0)
        {
            return false;
        }

        try
        {
            if ((attributes & FileAttributes.ReadOnly) != 0)
            {
                File.SetAttributes(
                    path,
                    attributes & ~FileAttributes.ReadOnly);
            }

            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: false);
            }
            else
            {
                File.Delete(path);
            }

            return true;
        }
        catch (Exception exception)
            when (IsRecoverableCleanupException(exception))
        {
            return false;
        }
    }

    private static bool IsDirectChild(
        string parent,
        string child)
    {
        var fullParent = Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(parent));
        var fullChild = Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(child));
        var childParent = Directory.GetParent(fullChild)?.FullName;

        return childParent is not null &&
               StringComparer.OrdinalIgnoreCase.Equals(
                   Path.TrimEndingDirectorySeparator(
                       Path.GetFullPath(childParent)),
                   fullParent);
    }

    private static bool IsReparsePoint(string path) =>
        (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
}
