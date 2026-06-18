using System.Text.Json;

namespace CodexSwitcher.Feasibility;

internal sealed record FileMetadata(long Length, DateTime LastWriteTimeUtc);

internal sealed record SnapshotComparison(
    bool IsFirstSnapshot,
    IReadOnlyList<string> Added,
    IReadOnlyList<string> Removed,
    IReadOnlyList<string> Changed);

internal sealed record SnapshotDocument(
    string CodexHome,
    Dictionary<string, FileMetadata> Files);

internal sealed class CodexHomeSnapshot
{
    private static readonly string[] ExcludedPrefixes =
    [
        ".sandbox/",
        ".sandbox-secrets/",
        ".tmp/",
        "tmp/"
    ];

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public SnapshotComparison CaptureAndCompare(FeasibilityPaths paths)
    {
        var current = Capture(paths.CodexHome);
        SnapshotDocument? previous = null;

        if (File.Exists(paths.SnapshotFile))
        {
            try
            {
                previous = JsonSerializer.Deserialize<SnapshotDocument>(
                    File.ReadAllText(paths.SnapshotFile),
                    JsonOptions);
            }
            catch (JsonException)
            {
                previous = null;
            }
        }

        AtomicFile.WriteAllText(
            paths.SnapshotFile,
            JsonSerializer.Serialize(
                new SnapshotDocument(paths.CodexHome, current),
                JsonOptions));

        if (previous is null ||
            !string.Equals(previous.CodexHome, paths.CodexHome, StringComparison.OrdinalIgnoreCase))
        {
            return new SnapshotComparison(true, [], [], []);
        }

        var added = current.Keys.Except(previous.Files.Keys, StringComparer.OrdinalIgnoreCase).Order().ToArray();
        var removed = previous.Files.Keys.Except(current.Keys, StringComparer.OrdinalIgnoreCase).Order().ToArray();
        var changed = current.Keys
            .Intersect(previous.Files.Keys, StringComparer.OrdinalIgnoreCase)
            .Where(path => current[path] != previous.Files[path])
            .Order()
            .ToArray();

        return new SnapshotComparison(false, added, removed, changed);
    }

    private static Dictionary<string, FileMetadata> Capture(string codexHome)
    {
        var result = new Dictionary<string, FileMetadata>(StringComparer.OrdinalIgnoreCase);
        if (!Directory.Exists(codexHome))
        {
            return result;
        }

        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            ReturnSpecialDirectories = false,
            AttributesToSkip = FileAttributes.ReparsePoint
        };

        foreach (var file in Directory.EnumerateFiles(codexHome, "*", options))
        {
            try
            {
                var info = new FileInfo(file);
                var relativePath = Path.GetRelativePath(codexHome, file)
                    .Replace(Path.DirectorySeparatorChar, '/');

                if (ExcludedPrefixes.Any(prefix =>
                        relativePath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                result[relativePath] = new FileMetadata(info.Length, info.LastWriteTimeUtc);
            }
            catch (Exception exception) when (
                exception is UnauthorizedAccessException or IOException)
            {
                // 조사 도구는 읽을 수 없는 파일 때문에 전체 조사를 중단하지 않는다.
            }
        }

        return result;
    }
}
