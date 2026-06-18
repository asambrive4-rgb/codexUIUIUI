using System.Text;
using CodexSwitcher.Infrastructure.Profiles;

namespace CodexSwitcher.Tests.Profiles;

[TestClass]
public sealed class WindowsLoginAuthenticationSessionTests
{
    private string _root = null!;
    private string _codexHome = null!;
    private string _recoveryRoot = null!;

    [TestInitialize]
    public void Initialize()
    {
        _root = Path.Combine(
            Path.GetTempPath(),
            $"CodexSwitcher.Login.Tests.{Guid.NewGuid():N}");
        _codexHome = Path.Combine(_root, "codex-home");
        _recoveryRoot = Path.Combine(_root, "recovery");
        Directory.CreateDirectory(_codexHome);
        File.WriteAllText(
            Path.Combine(_codexHome, "config.toml"),
            "cli_auth_credentials_store = \"file\"");
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [TestMethod]
    public async Task PrepareAndRestore_WithExistingCredential_RestoresBytes()
    {
        var original = Encoding.UTF8.GetBytes(
            """{"account":"original"}""");
        var authPath = AuthPath();
        await File.WriteAllBytesAsync(authPath, original);
        var session = CreateSession();

        await session.PrepareForLoginAsync(
            CancellationToken.None);

        Assert.IsFalse(File.Exists(authPath));
        Assert.IsTrue(
            await session.HasPendingRecoveryAsync(
                CancellationToken.None));

        await File.WriteAllTextAsync(
            authPath,
            """{"account":"new"}""");
        await session.RestorePreviousAsync(
            CancellationToken.None);

        CollectionAssert.AreEqual(
            original,
            await File.ReadAllBytesAsync(authPath));
    }

    [TestMethod]
    public async Task PrepareAndRestore_WithoutExistingCredential_RemovesNewCredential()
    {
        var session = CreateSession();

        await session.PrepareForLoginAsync(
            CancellationToken.None);
        await File.WriteAllTextAsync(
            AuthPath(),
            """{"account":"new"}""");
        await session.RestorePreviousAsync(
            CancellationToken.None);

        Assert.IsFalse(File.Exists(AuthPath()));
    }

    [TestMethod]
    public async Task PrepareForProfile_AppliesTargetAndCanRestorePrevious()
    {
        var original = "original"u8.ToArray();
        var target = "target"u8.ToArray();
        await File.WriteAllBytesAsync(AuthPath(), original);
        var session = CreateSession();

        await session.PrepareForProfileAsync(
            target,
            CancellationToken.None);

        CollectionAssert.AreEqual(
            target,
            await File.ReadAllBytesAsync(AuthPath()));
        await session.RestorePreviousAsync(
            CancellationToken.None);
        await session.ClearRecoveryAsync(
            CancellationToken.None);
        CollectionAssert.AreEqual(
            original,
            await File.ReadAllBytesAsync(AuthPath()));
    }

    [TestMethod]
    public async Task NewInstance_DetectsAndRestoresPendingRecovery()
    {
        var original = "original"u8.ToArray();
        await File.WriteAllBytesAsync(AuthPath(), original);
        await CreateSession().PrepareForLoginAsync(
            CancellationToken.None);

        var restarted = CreateSession();

        Assert.IsTrue(
            await restarted.HasPendingRecoveryAsync(
                CancellationToken.None));
        await restarted.RestorePreviousAsync(
            CancellationToken.None);
        await restarted.ClearRecoveryAsync(
            CancellationToken.None);

        CollectionAssert.AreEqual(
            original,
            await File.ReadAllBytesAsync(AuthPath()));
        Assert.IsFalse(
            await restarted.HasPendingRecoveryAsync(
                CancellationToken.None));
    }

    [TestMethod]
    public async Task ReadCurrentCredential_WhenMissing_ReturnsNull()
    {
        var credential = await CreateSession()
            .ReadCurrentCredentialAsync(CancellationToken.None);

        Assert.IsNull(credential);
    }

    [TestMethod]
    public async Task Prepare_WithKeyringConfiguration_IsRejected()
    {
        File.WriteAllText(
            Path.Combine(_codexHome, "config.toml"),
            "cli_auth_credentials_store = \"keyring\"");

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => CreateSession().PrepareForLoginAsync(
                CancellationToken.None));

        Assert.IsFalse(Directory.Exists(_recoveryRoot));
    }

    [TestMethod]
    public async Task Prepare_OnlyTouchesAuthFile()
    {
        var sharedData = Path.Combine(_codexHome, "history.jsonl");
        await File.WriteAllTextAsync(sharedData, "shared-work");
        await File.WriteAllTextAsync(AuthPath(), "credential");
        var session = CreateSession();

        await session.PrepareForLoginAsync(
            CancellationToken.None);
        await session.RestorePreviousAsync(
            CancellationToken.None);

        Assert.AreEqual(
            "shared-work",
            await File.ReadAllTextAsync(sharedData));
    }

    private WindowsLoginAuthenticationSession CreateSession()
    {
        return new WindowsLoginAuthenticationSession(
            _codexHome,
            _recoveryRoot);
    }

    private string AuthPath()
    {
        return Path.Combine(_codexHome, "auth.json");
    }
}
