using CodexSwitcher.Bootstrapper;

namespace CodexSwitcher.Tests.Presentation;

[TestClass]
public sealed class PopupPlacementStoreTests
{
    [TestMethod]
    public async Task SaveAndRead_RoundTripsPlacement()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            $"codex-switcher-popup-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var store = new PopupPlacementStore(
                Path.Combine(directory, "ui-settings.json"));

            await store.SaveAsync(
                123.5,
                456.25,
                CancellationToken.None);

            var restartedStore = new PopupPlacementStore(
                Path.Combine(directory, "ui-settings.json"));
            await restartedStore.LoadAsync(CancellationToken.None);
            var placement = restartedStore.ReadCached();

            Assert.IsNotNull(placement);
            Assert.AreEqual(123.5, placement.Left);
            Assert.AreEqual(456.25, placement.Top);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public async Task Save_UpdatesCacheBeforeDiskRoundTrip()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            $"codex-switcher-popup-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var store = new PopupPlacementStore(
                Path.Combine(directory, "ui-settings.json"));

            await store.SaveAsync(
                10,
                20,
                CancellationToken.None);

            var placement = store.ReadCached();

            Assert.IsNotNull(placement);
            Assert.AreEqual(10, placement.Left);
            Assert.AreEqual(20, placement.Top);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public async Task Load_WithCorruptFile_IgnoresStoredPlacement()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            $"codex-switcher-popup-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "ui-settings.json");
            await File.WriteAllTextAsync(
                path,
                "not-json");
            var store = new PopupPlacementStore(path);

            await store.LoadAsync(CancellationToken.None);

            Assert.IsNull(store.ReadCached());
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public async Task Save_WhenDiskWriteFails_KeepsCacheAndDoesNotThrow()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            $"codex-switcher-popup-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var store = new PopupPlacementStore(directory);

            await store.SaveAsync(
                30,
                40,
                CancellationToken.None);

            var placement = store.ReadCached();

            Assert.IsNotNull(placement);
            Assert.AreEqual(30, placement.Left);
            Assert.AreEqual(40, placement.Top);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
