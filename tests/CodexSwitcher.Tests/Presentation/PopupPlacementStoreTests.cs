using CodexSwitcher.Bootstrapper;

namespace CodexSwitcher.Tests.Presentation;

[TestClass]
public sealed class PopupPlacementStoreTests
{
    [TestMethod]
    public void SaveAndRead_RoundTripsPlacement()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            $"codex-switcher-popup-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var store = new PopupPlacementStore(
                Path.Combine(directory, "ui-settings.json"));

            store.Save(123.5, 456.25);
            var placement = store.Read();

            Assert.IsNotNull(placement);
            Assert.AreEqual(123.5, placement.Left);
            Assert.AreEqual(456.25, placement.Top);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
