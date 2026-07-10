using CodexSwitcher.Core.Usage;

namespace CodexSwitcher.Tests.Usage;

[TestClass]
public sealed class ProfileUsageRefreshPolicyTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 6, 19, 12, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public void IsDue_WithoutPreviousAttempt_IsTrue()
    {
        Assert.IsTrue(
            ProfileUsageRefreshPolicy.IsDue(
                Now,
                lastAttempt: null,
                isActive: true,
                consecutiveFailures: 0));
    }

    [TestMethod]
    public void IsDue_ActiveProfile_UsesTenSeconds()
    {
        Assert.IsFalse(
            ProfileUsageRefreshPolicy.IsDue(
                Now,
                Now - TimeSpan.FromSeconds(9),
                isActive: true,
                consecutiveFailures: 0));
        Assert.IsTrue(
            ProfileUsageRefreshPolicy.IsDue(
                Now,
                Now - TimeSpan.FromSeconds(10),
                isActive: true,
                consecutiveFailures: 0));
    }

    [TestMethod]
    public void IsDue_InactiveProfile_UsesFiveMinutes()
    {
        Assert.IsFalse(
            ProfileUsageRefreshPolicy.IsDue(
                Now,
                Now - TimeSpan.FromMinutes(4),
                isActive: false,
                consecutiveFailures: 0));
        Assert.IsTrue(
            ProfileUsageRefreshPolicy.IsDue(
                Now,
                Now - TimeSpan.FromMinutes(5),
                isActive: false,
                consecutiveFailures: 0));
    }

    [TestMethod]
    public void IsDue_AfterThreeActiveFailures_UsesOneMinute()
    {
        Assert.IsFalse(
            ProfileUsageRefreshPolicy.IsDue(
                Now,
                Now - TimeSpan.FromSeconds(59),
                isActive: true,
                consecutiveFailures: 3));
        Assert.IsTrue(
            ProfileUsageRefreshPolicy.IsDue(
                Now,
                Now - TimeSpan.FromMinutes(1),
                isActive: true,
                consecutiveFailures: 3));
    }

    [TestMethod]
    public void IsCacheFresh_AvailableInactive_UsesFiveMinutes()
    {
        Assert.IsTrue(
            ProfileUsageRefreshPolicy.IsCacheFresh(
                Now,
                Now - TimeSpan.FromMinutes(4),
                ProfileRateLimitStatus.Available,
                isActive: false));
        Assert.IsFalse(
            ProfileUsageRefreshPolicy.IsCacheFresh(
                Now,
                Now - TimeSpan.FromMinutes(5),
                ProfileRateLimitStatus.Available,
                isActive: false));
    }

    [TestMethod]
    public void IsCacheFresh_AvailableActive_UsesTenSeconds()
    {
        Assert.IsTrue(
            ProfileUsageRefreshPolicy.IsCacheFresh(
                Now,
                Now - TimeSpan.FromSeconds(9),
                ProfileRateLimitStatus.Available,
                isActive: true));
        Assert.IsFalse(
            ProfileUsageRefreshPolicy.IsCacheFresh(
                Now,
                Now - TimeSpan.FromSeconds(10),
                ProfileRateLimitStatus.Available,
                isActive: true));
    }

    [TestMethod]
    public void IsCacheFresh_Loading_IsNeverFresh()
    {
        Assert.IsFalse(
            ProfileUsageRefreshPolicy.IsCacheFresh(
                Now,
                Now,
                ProfileRateLimitStatus.Loading,
                isActive: true));
    }
}
