using CodexSwitcher.Infrastructure.Profiles;

namespace CodexSwitcher.Tests.Profiles;

[TestClass]
public sealed class WindowsCodexLoginControllerTests
{
    [TestMethod]
    [DataRow(
        @"C:\Program Files\WindowsApps\OpenAI.Codex_26.707.3563.0_x64__2p2nqsd0c76g0\app\ChatGPT.exe")]
    [DataRow(
        @"D:\WindowsApps\OpenAI.Codex_26.611.8604.0_x64__2p2nqsd0c76g0\app\Codex.exe")]
    public void IsCodexAppExecutablePath_AcceptsCurrentAndLegacyAppExecutables(
        string executablePath)
    {
        var result = WindowsCodexLoginController
            .IsCodexAppExecutablePath(executablePath);

        Assert.IsTrue(result);
    }

    [TestMethod]
    [DataRow(
        @"C:\Program Files\WindowsApps\OpenAI.Codex_26.707.3563.0_x64__2p2nqsd0c76g0\app\resources\codex.exe")]
    [DataRow(
        @"C:\Program Files\WindowsApps\OpenAI.ChatGPT_1.0.0.0_x64__publisher\app\ChatGPT.exe")]
    [DataRow(@"C:\Users\tester\AppData\Local\ChatGPT\ChatGPT.exe")]
    [DataRow("")]
    public void IsCodexAppExecutablePath_RejectsOtherExecutables(
        string executablePath)
    {
        var result = WindowsCodexLoginController
            .IsCodexAppExecutablePath(executablePath);

        Assert.IsFalse(result);
    }
}
