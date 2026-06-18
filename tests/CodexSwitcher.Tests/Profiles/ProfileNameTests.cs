using CodexSwitcher.Core.Profiles;

namespace CodexSwitcher.Tests.Profiles;

[TestClass]
public sealed class ProfileNameTests
{
    [TestMethod]
    public void Create_WithNull_ThrowsArgumentNullException()
    {
        Assert.ThrowsExactly<ArgumentNullException>(
            () => ProfileName.Create(null!));
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("   ")]
    [DataRow("\t\r\n")]
    public void Create_WithEmptyOrWhitespace_ThrowsArgumentException(string value)
    {
        Assert.ThrowsExactly<ArgumentException>(
            () => ProfileName.Create(value));
    }

    [TestMethod]
    public void Create_WithNormalValue_KeepsValue()
    {
        var profileName = ProfileName.Create("Work");

        Assert.AreEqual("Work", profileName.Value);
    }

    [TestMethod]
    public void Create_WithSurroundingWhitespace_TrimsValue()
    {
        var profileName = ProfileName.Create("  Personal  ");

        Assert.AreEqual("Personal", profileName.Value);
    }
}
