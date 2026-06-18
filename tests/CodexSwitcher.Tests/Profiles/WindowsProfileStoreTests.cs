using System.Diagnostics;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using CodexSwitcher.Core.Profiles;
using CodexSwitcher.Infrastructure.Profiles;

namespace CodexSwitcher.Tests.Profiles;

[TestClass]
public sealed class WindowsProfileStoreTests
{
    private string _testRoot = null!;
    private string _storageRoot = null!;

    [TestInitialize]
    public void Initialize()
    {
        _testRoot = Path.Combine(
            Path.GetTempPath(),
            $"CodexSwitcher.Tests.{Guid.NewGuid():N}");
        _storageRoot = Path.Combine(_testRoot, "profiles");
        Directory.CreateDirectory(_testRoot);
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(_testRoot))
        {
            Directory.Delete(_testRoot, recursive: true);
        }
    }

    [TestMethod]
    public async Task SaveAndRead_WithNewStore_RestoresProfileAndCredential()
    {
        var profile = CreateProfile("Work");
        var credential = Encoding.UTF8.GetBytes(
            """{"token":"sensitive-test-value"}""");
        var store = new WindowsProfileStore(_storageRoot);

        await store.SaveAsync(
            profile,
            credential,
            CancellationToken.None);

        var restartedStore = new WindowsProfileStore(_storageRoot);
        var result = await restartedStore.ReadAllAsync(
            CancellationToken.None);
        var restoredCredential =
            await restartedStore.ReadCredentialAsync(
                profile.Id,
                CancellationToken.None);

        Assert.HasCount(1, result.Profiles);
        Assert.AreEqual(profile, result.Profiles.Single());
        Assert.IsEmpty(result.Issues);
        CollectionAssert.AreEqual(credential, restoredCredential);

        var storedBytes = await File.ReadAllBytesAsync(
            CredentialPath(profile));
        Assert.IsFalse(storedBytes.SequenceEqual(credential));
        Assert.IsFalse(
            Encoding.UTF8.GetString(storedBytes)
                .Contains(
                    "sensitive-test-value",
                    StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task ReadAll_WithValidAndCorruptProfiles_ReturnsPartialResult()
    {
        var valid = CreateProfile("Valid");
        var corrupt = CreateProfile("Corrupt");
        var store = new WindowsProfileStore(_storageRoot);
        await store.SaveAsync(
            valid,
            "valid"u8.ToArray(),
            CancellationToken.None);
        await store.SaveAsync(
            corrupt,
            "corrupt"u8.ToArray(),
            CancellationToken.None);
        await File.WriteAllTextAsync(
            CredentialPath(corrupt),
            "not-a-protected-credential");

        var result = await store.ReadAllAsync(
            CancellationToken.None);

        Assert.HasCount(1, result.Profiles);
        Assert.AreEqual(valid, result.Profiles.Single());
        Assert.HasCount(1, result.Issues);
        Assert.AreEqual(
            ProfileStoreIssueCode.UnreadableCredential,
            result.Issues.Single().Code);
    }

    [TestMethod]
    public async Task ReadAll_WithCorruptMetadata_ReportsSafeErrorCode()
    {
        var profile = CreateProfile("Work");
        var store = new WindowsProfileStore(_storageRoot);
        await store.SaveAsync(
            profile,
            "credential"u8.ToArray(),
            CancellationToken.None);
        await File.WriteAllTextAsync(
            MetadataPath(profile),
            """{"schemaVersion":1,"id":"invalid","name":"secret"}""");

        var result = await store.ReadAllAsync(
            CancellationToken.None);

        Assert.IsEmpty(result.Profiles);
        Assert.HasCount(1, result.Issues);
        Assert.AreEqual(
            ProfileStoreIssueCode.CorruptMetadata,
            result.Issues.Single().Code);
        Assert.AreEqual(
            nameof(ProfileStoreIssueCode.CorruptMetadata),
            result.Issues.Single().Code.ToString());
    }

    [TestMethod]
    public async Task ReadAll_WithMetadataIdMismatch_ReportsMismatch()
    {
        var profile = CreateProfile("Work");
        var differentId = ProfileId.New();
        var store = new WindowsProfileStore(_storageRoot);
        await store.SaveAsync(
            profile,
            "credential"u8.ToArray(),
            CancellationToken.None);
        await File.WriteAllTextAsync(
            MetadataPath(profile),
            $$"""
            {
              "schemaVersion": 1,
              "id": "{{differentId}}",
              "name": "Work"
            }
            """);

        var result = await store.ReadAllAsync(
            CancellationToken.None);

        Assert.IsEmpty(result.Profiles);
        Assert.HasCount(1, result.Issues);
        Assert.AreEqual(
            ProfileStoreIssueCode.ProfileIdMismatch,
            result.Issues.Single().Code);
    }

    [TestMethod]
    public async Task ReadAll_WithMissingCredential_ReportsMissingCredential()
    {
        var profile = CreateProfile("Work");
        var store = new WindowsProfileStore(_storageRoot);
        await store.SaveAsync(
            profile,
            "credential"u8.ToArray(),
            CancellationToken.None);
        File.Delete(CredentialPath(profile));

        var result = await store.ReadAllAsync(
            CancellationToken.None);

        Assert.IsEmpty(result.Profiles);
        Assert.HasCount(1, result.Issues);
        Assert.AreEqual(
            ProfileStoreIssueCode.MissingCredential,
            result.Issues.Single().Code);
    }

    [TestMethod]
    public async Task Save_WhenIdAlreadyExists_PreservesExistingProfile()
    {
        var original = CreateProfile("Original");
        var replacement = new Profile(
            original.Id,
            ProfileName.Create("Replacement"));
        var store = new WindowsProfileStore(_storageRoot);
        await store.SaveAsync(
            original,
            "original-credential"u8.ToArray(),
            CancellationToken.None);

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => store.SaveAsync(
                replacement,
                "replacement-credential"u8.ToArray(),
                CancellationToken.None));

        var result = await store.ReadAllAsync(
            CancellationToken.None);
        var credential = await store.ReadCredentialAsync(
            original.Id,
            CancellationToken.None);

        Assert.AreEqual(original, result.Profiles.Single());
        CollectionAssert.AreEqual(
            "original-credential"u8.ToArray(),
            credential);
    }

    [TestMethod]
    public async Task ReadAll_IgnoresInterruptedTemporaryDirectory()
    {
        var store = new WindowsProfileStore(_storageRoot);
        _ = await store.ReadAllAsync(CancellationToken.None);
        var temporary = Path.Combine(
            _storageRoot,
            $".pending-{Guid.NewGuid():D}");
        Directory.CreateDirectory(temporary);
        await File.WriteAllTextAsync(
            Path.Combine(temporary, "garbage.txt"),
            "garbage");

        var result = await store.ReadAllAsync(
            CancellationToken.None);

        Assert.IsEmpty(result.Profiles);
        Assert.IsEmpty(result.Issues);
    }

    [TestMethod]
    public async Task Delete_RemovesOnlySelectedProfile()
    {
        var first = CreateProfile("First");
        var second = CreateProfile("Second");
        var outsideSentinel = Path.Combine(
            _testRoot,
            "outside-sentinel.txt");
        await File.WriteAllTextAsync(
            outsideSentinel,
            "do-not-delete");
        var store = new WindowsProfileStore(_storageRoot);
        await store.SaveAsync(
            first,
            "first"u8.ToArray(),
            CancellationToken.None);
        await store.SaveAsync(
            second,
            "second"u8.ToArray(),
            CancellationToken.None);

        await store.DeleteAsync(
            first.Id,
            CancellationToken.None);

        var result = await store.ReadAllAsync(
            CancellationToken.None);
        Assert.HasCount(1, result.Profiles);
        Assert.AreEqual(second, result.Profiles.Single());
        Assert.IsTrue(File.Exists(outsideSentinel));
    }

    [TestMethod]
    public async Task Delete_WithReparsePoint_RefusesDeletion()
    {
        var profile = CreateProfile("Work");
        var store = new WindowsProfileStore(_storageRoot);
        await store.SaveAsync(
            profile,
            "credential"u8.ToArray(),
            CancellationToken.None);
        var outside = Path.Combine(_testRoot, "outside");
        Directory.CreateDirectory(outside);
        var sentinel = Path.Combine(outside, "sentinel.txt");
        await File.WriteAllTextAsync(sentinel, "keep");
        var link = Path.Combine(ProfileDirectory(profile), "linked");
        CreateDirectoryReparsePoint(link, outside);

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => store.DeleteAsync(
                profile.Id,
                CancellationToken.None));

        Assert.IsTrue(File.Exists(sentinel));
        Assert.IsTrue(Directory.Exists(ProfileDirectory(profile)));
        Directory.Delete(link);
    }

    [TestMethod]
    public async Task Delete_WithUnexpectedProfileFile_RefusesDeletion()
    {
        var profile = CreateProfile("Work");
        var store = new WindowsProfileStore(_storageRoot);
        await store.SaveAsync(
            profile,
            "credential"u8.ToArray(),
            CancellationToken.None);
        var unexpected = Path.Combine(
            ProfileDirectory(profile),
            "unexpected.txt");
        await File.WriteAllTextAsync(unexpected, "keep");

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => store.DeleteAsync(
                profile.Id,
                CancellationToken.None));

        Assert.IsTrue(File.Exists(unexpected));
        Assert.IsTrue(File.Exists(MetadataPath(profile)));
        Assert.IsTrue(File.Exists(CredentialPath(profile)));
        Assert.IsTrue(Directory.Exists(ProfileDirectory(profile)));
    }

    [TestMethod]
    public async Task StorageDirectories_AllowOnlyCurrentUserAndSystem()
    {
        var profile = CreateProfile("Work");
        var store = new WindowsProfileStore(_storageRoot);
        await store.SaveAsync(
            profile,
            "credential"u8.ToArray(),
            CancellationToken.None);

        using var identity = WindowsIdentity.GetCurrent();
        var currentUser = identity.User
            ?? throw new InvalidOperationException(
                "현재 Windows 사용자 SID가 없습니다.");
        var system = new SecurityIdentifier(
            WellKnownSidType.LocalSystemSid,
            domainSid: null);

        AssertProtectedDirectory(
            _storageRoot,
            currentUser,
            system);
        AssertProtectedDirectory(
            ProfileDirectory(profile),
            currentUser,
            system);
        AssertFileAccessIsRestricted(
            MetadataPath(profile),
            currentUser,
            system);
        AssertFileAccessIsRestricted(
            CredentialPath(profile),
            currentUser,
            system);
    }

    [TestMethod]
    public async Task ReadAll_WhenCanceled_DoesNotCreateStorage()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var store = new WindowsProfileStore(_storageRoot);

        await Assert.ThrowsExactlyAsync<OperationCanceledException>(
            () => store.ReadAllAsync(cancellation.Token));

        Assert.IsFalse(Directory.Exists(_storageRoot));
    }

    private Profile CreateProfile(string name)
    {
        return new Profile(
            ProfileId.New(),
            ProfileName.Create(name));
    }

    private string ProfileDirectory(Profile profile)
    {
        return Path.Combine(_storageRoot, profile.Id.ToString());
    }

    private string MetadataPath(Profile profile)
    {
        return Path.Combine(
            ProfileDirectory(profile),
            "profile.json");
    }

    private string CredentialPath(Profile profile)
    {
        return Path.Combine(
            ProfileDirectory(profile),
            "credential.bin");
    }

    private static void AssertProtectedDirectory(
        string path,
        SecurityIdentifier currentUser,
        SecurityIdentifier system)
    {
        var security = new DirectoryInfo(path).GetAccessControl();
        Assert.IsTrue(security.AreAccessRulesProtected);
        AssertOnlyExpectedAllowRules(
            security.GetAccessRules(
                includeExplicit: true,
                includeInherited: true,
                typeof(SecurityIdentifier)),
            currentUser,
            system);
    }

    private static void AssertFileAccessIsRestricted(
        string path,
        SecurityIdentifier currentUser,
        SecurityIdentifier system)
    {
        var security = new FileInfo(path).GetAccessControl();
        AssertOnlyExpectedAllowRules(
            security.GetAccessRules(
                includeExplicit: true,
                includeInherited: true,
                typeof(SecurityIdentifier)),
            currentUser,
            system);
    }

    private static void AssertOnlyExpectedAllowRules(
        AuthorizationRuleCollection rules,
        SecurityIdentifier currentUser,
        SecurityIdentifier system)
    {
        var allowRules = rules
            .Cast<FileSystemAccessRule>()
            .Where(rule =>
                rule.AccessControlType == AccessControlType.Allow)
            .ToArray();

        Assert.IsTrue(
            allowRules.All(
                rule =>
                    Equals(rule.IdentityReference, currentUser) ||
                    Equals(rule.IdentityReference, system)));
        Assert.IsTrue(
            allowRules.Any(
                rule => Equals(
                    rule.IdentityReference,
                    currentUser)));
        Assert.IsTrue(
            allowRules.Any(
                rule => Equals(
                    rule.IdentityReference,
                    system)));
    }

    private static void CreateDirectoryReparsePoint(
        string link,
        string target)
    {
        try
        {
            Directory.CreateSymbolicLink(link, target);
            return;
        }
        catch (IOException)
        {
            // 개발자 모드가 꺼진 Windows에서도 junction은 일반 사용자로 만들 수 있다.
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = Environment.GetEnvironmentVariable("ComSpec")
                ?? "cmd.exe",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        startInfo.ArgumentList.Add("/d");
        startInfo.ArgumentList.Add("/c");
        startInfo.ArgumentList.Add("mklink");
        startInfo.ArgumentList.Add("/J");
        startInfo.ArgumentList.Add(link);
        startInfo.ArgumentList.Add(target);

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException(
                "재분석 지점 테스트 프로세스를 시작하지 못했습니다.");
        process.WaitForExit();

        Assert.AreEqual(
            0,
            process.ExitCode,
            process.StandardError.ReadToEnd());
    }
}
