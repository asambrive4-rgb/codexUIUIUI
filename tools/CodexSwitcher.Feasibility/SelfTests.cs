using System.Security.Cryptography;
using System.Text;

namespace CodexSwitcher.Feasibility;

internal static class SelfTests
{
    public static void Run()
    {
        var root = Path.Combine(Path.GetTempPath(), $"CodexSwitcher.Feasibility.Tests.{Guid.NewGuid():N}");

        try
        {
            Directory.CreateDirectory(root);
            TestSlotValidation();
            TestDataProtectionRoundTrip();
            TestCaptureActivateAndRestore(root);
            TestStoreManifestParsing(root);
            TestStoreProbeParsing();
            TestSnapshotSeparatesCodexHomes(root);
            TestProcessTreeInspection();
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private static void TestSlotValidation()
    {
        AuthProfileStore.ValidateSlot("account-a");
        AssertThrows<ArgumentException>(() => AuthProfileStore.ValidateSlot(""));
        AssertThrows<ArgumentException>(() => AuthProfileStore.ValidateSlot("../escape"));
        AssertThrows<ArgumentException>(() => AuthProfileStore.ValidateSlot("개인계정"));
    }

    private static void TestDataProtectionRoundTrip()
    {
        var protector = new CurrentUserDataProtector();
        var plaintext = RandomNumberGenerator.GetBytes(128);
        var protectedBytes = protector.Protect(plaintext);
        var restored = protector.Unprotect(protectedBytes);

        try
        {
            Assert(
                plaintext.Length == restored.Length &&
                CryptographicOperations.FixedTimeEquals(plaintext, restored),
                "DPAPI 왕복 결과가 원본과 다릅니다.");
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
            CryptographicOperations.ZeroMemory(protectedBytes);
            CryptographicOperations.ZeroMemory(restored);
        }
    }

    private static void TestCaptureActivateAndRestore(string root)
    {
        var codexHome = Path.Combine(root, "codex-home");
        var storage = Path.Combine(root, "storage");
        Directory.CreateDirectory(codexHome);
        var paths = new FeasibilityPaths(codexHome, storage);
        var store = new AuthProfileStore(paths, new CurrentUserDataProtector());
        var first = Encoding.UTF8.GetBytes("{\"test\":\"first\"}");
        var second = Encoding.UTF8.GetBytes("{\"test\":\"second\"}");

        AtomicFile.WriteAllBytes(paths.AuthFile, first);
        store.Capture("first");
        var originalCaptured = store.PrepareLogin();
        Assert(originalCaptured, "최초 인증 상태가 보관되지 않았습니다.");
        Assert(!File.Exists(paths.AuthFile), "로그인 준비 후 테스트 인증 파일이 남아 있습니다.");
        store.RestoreRecovery();
        Assert(
            File.ReadAllBytes(paths.AuthFile).SequenceEqual(first),
            "로그인 준비 실패 상황의 직전 인증 복구에 실패했습니다.");

        originalCaptured = store.PrepareLogin();
        Assert(!originalCaptured, "최초 인증 상태를 중복 보관했습니다.");

        AtomicFile.WriteAllBytes(paths.AuthFile, second);
        store.Capture("second");
        store.Activate("first");
        Assert(File.ReadAllBytes(paths.AuthFile).SequenceEqual(first), "첫 슬롯 활성화에 실패했습니다.");
        Assert(store.FindExactCurrentSlot() == "first", "현재 슬롯의 불투명 비교에 실패했습니다.");

        store.Activate("second");
        Assert(File.ReadAllBytes(paths.AuthFile).SequenceEqual(second), "둘째 슬롯 활성화에 실패했습니다.");
        store.VerifyRollbackOnInvalidCredential();
        Assert(
            File.ReadAllBytes(paths.AuthFile).SequenceEqual(second),
            "손상 슬롯 실패 주입 후 둘째 인증 상태가 복구되지 않았습니다.");

        store.RestoreOriginal();
        Assert(File.ReadAllBytes(paths.AuthFile).SequenceEqual(first), "최초 인증 복구에 실패했습니다.");
    }

    private static void TestStoreManifestParsing(string root)
    {
        var packageRoot = Path.Combine(root, "OpenAI.Codex_1.2.3.4_x64__publisher");
        Directory.CreateDirectory(Path.Combine(packageRoot, "app"));
        var manifestPath = Path.Combine(packageRoot, "AppxManifest.xml");
        File.WriteAllText(
            manifestPath,
            """
            <?xml version="1.0" encoding="utf-8"?>
            <Package xmlns="http://schemas.microsoft.com/appx/manifest/foundation/windows10">
              <Identity Name="OpenAI.Codex" Publisher="CN=Test" Version="1.2.3.4" ProcessorArchitecture="x64" />
              <Applications>
                <Application Id="App" Executable="app/Codex.exe" EntryPoint="Windows.FullTrustApplication" />
              </Applications>
            </Package>
            """);

        var installation = CodexInstallationLocator.ParseStoreManifest(packageRoot, manifestPath);
        Assert(installation.Kind == CodexInstallKind.MicrosoftStore, "Store 설치 유형 판별에 실패했습니다.");
        Assert(
            installation.AppUserModelId == "OpenAI.Codex_publisher!App",
            "AUMID 생성 결과가 예상과 다릅니다.");
    }

    private static void TestSnapshotSeparatesCodexHomes(string root)
    {
        var storage = Path.Combine(root, "snapshot-storage");
        var firstHome = Path.Combine(root, "snapshot-home-a");
        var secondHome = Path.Combine(root, "snapshot-home-b");
        Directory.CreateDirectory(firstHome);
        Directory.CreateDirectory(secondHome);
        File.WriteAllText(Path.Combine(firstHome, "one.txt"), "one");
        File.WriteAllText(Path.Combine(secondHome, "two.txt"), "two");

        var snapshot = new CodexHomeSnapshot();
        var first = snapshot.CaptureAndCompare(new FeasibilityPaths(firstHome, storage));
        var second = snapshot.CaptureAndCompare(new FeasibilityPaths(secondHome, storage));

        Assert(first.IsFirstSnapshot, "첫 Codex 홈 기준점이 생성되지 않았습니다.");
        Assert(second.IsFirstSnapshot, "서로 다른 Codex 홈의 기준점이 섞였습니다.");
    }

    private static void TestStoreProbeParsing()
    {
        var installation = CodexInstallationLocator.ParseStoreProbeJson(
            """
            {
              "Version": "26.611.8604.0",
              "PackageFamilyName": "OpenAI.Codex_2p2nqsd0c76g0",
              "InstallLocation": "C:\\Program Files\\WindowsApps\\OpenAI.Codex_26.611.8604.0_x64__2p2nqsd0c76g0",
              "AppUserModelId": "OpenAI.Codex_2p2nqsd0c76g0!App"
            }
            """);

        Assert(installation.Kind == CodexInstallKind.MicrosoftStore, "Store 조사 JSON 판별에 실패했습니다.");
        Assert(installation.Version == "26.611.8604.0", "Store 조사 버전 판별에 실패했습니다.");
        Assert(
            installation.AppUserModelId == "OpenAI.Codex_2p2nqsd0c76g0!App",
            "Store 조사 AUMID 판별에 실패했습니다.");
    }

    private static void TestProcessTreeInspection()
    {
        var processes = new Dictionary<int, ProcessTreeEntry>
        {
            [100] = new ProcessTreeEntry(100, 50, "dotnet.exe"),
            [50] = new ProcessTreeEntry(50, 20, "powershell.exe"),
            [20] = new ProcessTreeEntry(20, 1, "Codex.exe"),
            [1] = new ProcessTreeEntry(1, 0, "system.exe")
        };

        var integrated = ProcessTreeInspector.Inspect(100, processes);
        Assert(integrated.IsDescendantOfCodex, "Codex 통합 터미널 계보를 감지하지 못했습니다.");
        Assert(integrated.CodexAncestorProcessId == 20, "Codex 조상 PID 판별에 실패했습니다.");

        var external = ProcessTreeInspector.Inspect(
            100,
            new Dictionary<int, ProcessTreeEntry>
            {
                [100] = new ProcessTreeEntry(100, 50, "dotnet.exe"),
                [50] = new ProcessTreeEntry(50, 1, "powershell.exe"),
                [1] = new ProcessTreeEntry(1, 0, "explorer.exe")
            });
        Assert(!external.IsDescendantOfCodex, "외부 PowerShell을 Codex 내부로 잘못 판별했습니다.");
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private static void AssertThrows<TException>(Action action)
        where TException : Exception
    {
        try
        {
            action();
        }
        catch (TException)
        {
            return;
        }

        throw new InvalidOperationException($"{typeof(TException).Name} 예외가 발생하지 않았습니다.");
    }
}
