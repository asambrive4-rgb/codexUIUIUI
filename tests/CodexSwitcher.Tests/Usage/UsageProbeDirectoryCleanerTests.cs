using CodexSwitcher.Infrastructure.Usage;

namespace CodexSwitcher.Tests.Usage;

[TestClass]
public sealed class UsageProbeDirectoryCleanerTests
{
    [TestMethod]
    public void TryDeleteSessionDirectory_RemovesReadOnlyFiles()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var session = Directory.CreateDirectory(
                Path.Combine(root, "session")).FullName;
            var nested = Directory.CreateDirectory(
                Path.Combine(session, "nested")).FullName;
            var file = Path.Combine(nested, "readonly.txt");
            File.WriteAllText(file, "stale cache");
            File.SetAttributes(
                file,
                File.GetAttributes(file) | FileAttributes.ReadOnly);

            var deleted =
                UsageProbeDirectoryCleaner.TryDeleteSessionDirectory(
                    root,
                    session);

            Assert.IsTrue(deleted);
            Assert.IsFalse(Directory.Exists(session));
        }
        finally
        {
            DeleteTemporaryDirectory(root);
        }
    }

    [TestMethod]
    public void TryDeleteSessionDirectory_WhenFileIsLocked_DoesNotThrow()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var session = Directory.CreateDirectory(
                Path.Combine(root, "session")).FullName;
            var file = Path.Combine(session, "locked.txt");
            File.WriteAllText(file, "locked cache");

            using var locked = new FileStream(
                file,
                FileMode.Open,
                FileAccess.Read,
                FileShare.None);

            var deleted =
                UsageProbeDirectoryCleaner.TryDeleteSessionDirectory(
                    root,
                    session);

            Assert.IsFalse(deleted);
            Assert.IsTrue(Directory.Exists(session));
        }
        finally
        {
            DeleteTemporaryDirectory(root);
        }
    }

    [TestMethod]
    public void TryDeleteSessionDirectory_DoesNotDeleteOutsideProbeRoot()
    {
        var root = CreateTemporaryDirectory();
        var outside = CreateTemporaryDirectory();
        try
        {
            File.WriteAllText(
                Path.Combine(outside, "keep.txt"),
                "outside");

            var deleted =
                UsageProbeDirectoryCleaner.TryDeleteSessionDirectory(
                    root,
                    outside);

            Assert.IsFalse(deleted);
            Assert.IsTrue(Directory.Exists(outside));
        }
        finally
        {
            DeleteTemporaryDirectory(root);
            DeleteTemporaryDirectory(outside);
        }
    }

    private static string CreateTemporaryDirectory()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "CodexSwitcher.Tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static void DeleteTemporaryDirectory(string directory)
    {
        if (!Directory.Exists(directory))
        {
            return;
        }

        foreach (var entry in Directory.EnumerateFileSystemEntries(
                     directory,
                     "*",
                     SearchOption.AllDirectories))
        {
            if ((File.GetAttributes(entry) & FileAttributes.ReadOnly) != 0)
            {
                File.SetAttributes(
                    entry,
                    File.GetAttributes(entry) & ~FileAttributes.ReadOnly);
            }
        }

        Directory.Delete(directory, recursive: true);
    }
}
