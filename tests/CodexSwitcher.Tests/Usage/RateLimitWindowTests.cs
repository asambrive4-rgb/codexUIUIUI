using CodexSwitcher.Core.Usage;

namespace CodexSwitcher.Tests.Usage;

[TestClass]
public sealed class RateLimitWindowTests
{
    [TestMethod]
    [DataRow(100, 0, RateLimitDisplayLevel.Dead)]
    [DataRow(99, 1, RateLimitDisplayLevel.Danger)]
    [DataRow(81, 19, RateLimitDisplayLevel.Danger)]
    [DataRow(80, 20, RateLimitDisplayLevel.Warning)]
    [DataRow(61, 39, RateLimitDisplayLevel.Warning)]
    [DataRow(60, 40, RateLimitDisplayLevel.Healthy)]
    [DataRow(0, 100, RateLimitDisplayLevel.Healthy)]
    public void RemainingPercent_UsesExactDisplayBoundaries(
        int usedPercent,
        int expectedRemaining,
        RateLimitDisplayLevel expectedLevel)
    {
        var window = new RateLimitWindow(
            usedPercent,
            WindowDurationMinutes: 300,
            ResetsAt: null);

        Assert.AreEqual(
            expectedRemaining,
            window.RemainingPercent);
        Assert.AreEqual(
            expectedLevel,
            window.DisplayLevel);
    }

    [TestMethod]
    [DataRow(-20, 100)]
    [DataRow(150, 0)]
    public void RemainingPercent_ClampsToValidRange(
        int usedPercent,
        int expectedRemaining)
    {
        var window = new RateLimitWindow(
            usedPercent,
            WindowDurationMinutes: null,
            ResetsAt: null);

        Assert.AreEqual(
            expectedRemaining,
            window.RemainingPercent);
    }
}
