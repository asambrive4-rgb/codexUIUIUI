using CodexSwitcher.Core.Profiles;

namespace CodexSwitcher.Tests.Profiles;

[TestClass]
public sealed class ProfileIdTests
{
    [TestMethod]
    public void New_CreatesNonEmptyUniqueIds()
    {
        var first = ProfileId.New();
        var second = ProfileId.New();

        Assert.AreNotEqual(Guid.Empty, first.Value);
        Assert.AreNotEqual(first, second);
    }

    [TestMethod]
    public void TryParse_WithCanonicalGuid_RestoresId()
    {
        var original = ProfileId.New();

        var parsed = ProfileId.TryParse(
            original.ToString(),
            out var restored);

        Assert.IsTrue(parsed);
        Assert.AreEqual(original, restored);
    }

    [TestMethod]
    public void From_WithEmptyGuid_ThrowsArgumentException()
    {
        Assert.ThrowsExactly<ArgumentException>(
            () => ProfileId.From(Guid.Empty));
    }

    [TestMethod]
    public void Profile_WithEmptyId_ThrowsArgumentException()
    {
        Assert.ThrowsExactly<ArgumentException>(
            () => new Profile(
                default,
                ProfileName.Create("Work")));
    }
}
