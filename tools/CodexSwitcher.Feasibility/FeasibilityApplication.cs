namespace CodexSwitcher.Feasibility;

internal sealed class FeasibilityApplication
{
    private readonly FeasibilityPaths _paths;
    private readonly AuthStorageInspector _authStorageInspector;
    private readonly CodexInstallationLocator _installationLocator;
    private readonly CodexProcessController _processController;
    private readonly CodexHomeSnapshot _snapshot;
    private readonly AuthProfileStore _profileStore;
    private readonly CodexLauncher _launcher;

    private FeasibilityApplication(
        FeasibilityPaths paths,
        AuthStorageInspector authStorageInspector,
        CodexInstallationLocator installationLocator,
        CodexProcessController processController,
        CodexHomeSnapshot snapshot,
        AuthProfileStore profileStore,
        CodexLauncher launcher)
    {
        _paths = paths;
        _authStorageInspector = authStorageInspector;
        _installationLocator = installationLocator;
        _processController = processController;
        _snapshot = snapshot;
        _profileStore = profileStore;
        _launcher = launcher;
    }

    public static FeasibilityApplication CreateDefault()
    {
        var paths = FeasibilityPaths.CreateDefault();
        return new FeasibilityApplication(
            paths,
            new AuthStorageInspector(),
            new CodexInstallationLocator(),
            new CodexProcessController(
                new ProcessTreeInspector(),
                new RestartManagerShutdown()),
            new CodexHomeSnapshot(),
            new AuthProfileStore(paths, new CurrentUserDataProtector()),
            new CodexLauncher());
    }

    public async Task<int> RunAsync(string[] args)
    {
        if (args.Length == 0)
        {
            PrintHelp();
            return 2;
        }

        try
        {
            return args[0].ToLowerInvariant() switch
            {
                "inspect" => Inspect(),
                "capture" => Capture(args),
                "prepare-login" => PrepareLogin(),
                "activate" => Activate(args),
                "restore-original" => RestoreOriginal(),
                "verify" => Verify(),
                "rollback-test" => RollbackTest(),
                "launch" => Launch(),
                "close" => await CloseAsync(args),
                "self-test" => SelfTest(),
                "help" or "--help" or "-h" => Help(),
                _ => UnknownCommand(args[0])
            };
        }
        catch (CodexRunningException exception)
        {
            WriteError(exception.Message);
            WriteError("먼저 `close`를 실행하고 종료 여부를 확인하세요. 이 도구는 강제 종료하지 않습니다.");
            return 4;
        }
        catch (Exception exception) when (
            exception is ArgumentException or
            FileNotFoundException or
            InvalidDataException or
            InvalidOperationException or
            IOException or
            UnauthorizedAccessException)
        {
            WriteError(exception.Message);
            return 3;
        }
    }

    private int Inspect()
    {
        var installation = _installationLocator.Locate();
        var authStorage = _authStorageInspector.Inspect(_paths);
        var processes = _processController.Inspect();
        var comparison = _snapshot.CaptureAndCompare(_paths);

        Console.WriteLine("Codex Account Switcher 0단계 조사");
        Console.WriteLine($"- 설치 유형: {DescribeInstallation(installation)}");
        Console.WriteLine($"- Store AUMID: {installation.AppUserModelId ?? "확인되지 않음"}");
        Console.WriteLine($"- 인증 저장: {authStorage.Description}");
        Console.WriteLine($"- Codex 프로세스: {processes.ProcessCount}개");
        Console.WriteLine($"- 정상 종료 요청 가능 창: {(processes.HasCloseableWindow ? "관찰됨" : "관찰되지 않음")}");
        PrintSnapshotComparison(comparison);

        if (!authStorage.SupportsOpaqueFileSwitching)
        {
            WriteError("현재 환경에서는 auth.json 불투명 교체 실험을 진행하면 안 됩니다.");
            return 3;
        }

        return installation.IsSupportedStoreInstallation ? 0 : 3;
    }

    private int Capture(string[] args)
    {
        EnsureSingleArgument(args, "capture <slot>");
        EnsureFileSwitchingSupported();
        _processController.EnsureStopped();
        _profileStore.Capture(args[1]);
        Console.WriteLine($"'{args[1]}' 슬롯에 현재 인증 상태를 암호화해 저장했습니다.");
        return 0;
    }

    private int PrepareLogin()
    {
        EnsureFileSwitchingSupported();
        _processController.EnsureStopped();
        var capturedOriginal = _profileStore.PrepareLogin();
        Console.WriteLine(capturedOriginal
            ? "최초 인증 상태를 보관하고 새 로그인 상태를 준비했습니다."
            : "복구용 인증 상태를 갱신하고 새 로그인 상태를 준비했습니다.");
        try
        {
            Launch();
        }
        catch
        {
            _profileStore.RestoreRecovery();
            throw;
        }

        Console.WriteLine("Codex 로그인 화면에서 시험 계정 로그인을 완료하세요.");
        return 0;
    }

    private int Activate(string[] args)
    {
        EnsureSingleArgument(args, "activate <slot>");
        EnsureFileSwitchingSupported();
        _processController.EnsureStopped();
        _profileStore.Activate(args[1]);
        Console.WriteLine($"'{args[1]}' 슬롯의 인증 상태를 적용했습니다.");
        try
        {
            Launch();
        }
        catch
        {
            _profileStore.RestoreRecovery();
            throw;
        }

        return 0;
    }

    private int RestoreOriginal()
    {
        EnsureFileSwitchingSupported();
        _processController.EnsureStopped();
        _profileStore.RestoreOriginal();
        Console.WriteLine("실험 전 최초 인증 상태를 복구했습니다.");
        return 0;
    }

    private int Verify()
    {
        var installation = _installationLocator.Locate();
        var authStorage = _authStorageInspector.Inspect(_paths);
        var processes = _processController.Inspect();
        var slots = _profileStore.GetSlots();
        var exactSlot = _profileStore.FindExactCurrentSlot();

        Console.WriteLine("0단계 검증 상태");
        Console.WriteLine($"- Store 설치 탐지: {(installation.IsSupportedStoreInstallation ? "성공" : "실패")}");
        Console.WriteLine($"- 파일 인증 교체 지원: {(authStorage.SupportsOpaqueFileSwitching ? "가능" : "중단 필요")}");
        Console.WriteLine($"- 저장 슬롯 수: {slots.Count}");
        Console.WriteLine($"- 현재 인증의 정확한 슬롯 일치: {exactSlot ?? "일치 없음 또는 확인 불가"}");
        Console.WriteLine($"- Codex 실행 상태: {(processes.IsRunning ? $"실행 중({processes.ProcessCount}개)" : "종료됨")}");
        Console.WriteLine("- 공통 작업 데이터와 ChatGPT 웹 세션 보존: 수동 확인 필요");

        return installation.IsSupportedStoreInstallation &&
               authStorage.SupportsOpaqueFileSwitching
            ? 0
            : 3;
    }

    private int RollbackTest()
    {
        EnsureFileSwitchingSupported();
        _processController.EnsureStopped();
        _profileStore.VerifyRollbackOnInvalidCredential();
        Console.WriteLine("손상된 인증 보관본 적용 실패 후 직전 인증 상태 복구를 확인했습니다.");
        return 0;
    }

    private int Launch()
    {
        var installation = _installationLocator.Locate();
        _launcher.Launch(installation);
        Console.WriteLine("Store AUMID를 사용해 Codex 실행을 요청했습니다.");
        return 0;
    }

    private async Task<int> CloseAsync(string[] args)
    {
        if (args.Length == 2 &&
            string.Equals(args[1], "--force", StringComparison.OrdinalIgnoreCase))
        {
            var remaining = _processController.ForceStop();
            if (remaining == 0)
            {
                Console.WriteLine("사용자의 명시적 승인에 따라 Codex를 강제 종료했습니다.");
                return 0;
            }

            WriteError($"강제 종료 후에도 Codex 프로세스 {remaining}개가 남아 있습니다.");
            return 5;
        }

        if (args.Length != 1)
        {
            throw new ArgumentException("사용법: close 또는 close --force");
        }

        var result = await _processController.RequestNormalCloseAsync(TimeSpan.FromSeconds(10));

        if (!result.WasRunning)
        {
            Console.WriteLine("실행 중인 Codex가 없습니다.");
            return 0;
        }

        if (result.ExitedWithinTimeout)
        {
            Console.WriteLine("Codex 정상 종료를 확인했습니다.");
            return 0;
        }

        WriteError(
            result.CloseRequested
                ? $"정상 종료({result.Strategy})를 요청했지만 끝나지 않았습니다. 남은 프로세스: {result.RemainingProcessCount}개"
                : "정상 종료를 요청할 수 있는 창을 찾지 못했습니다. 강제 종료는 자동으로 수행하지 않습니다.");
        if (result.NativeErrorCode is int errorCode)
        {
            WriteError(
                $"Windows Restart Manager 오류 {errorCode}: {RestartManagerShutdown.DescribeError(errorCode)}");
        }

        WriteError("작업 손실 가능성을 확인한 뒤 `close --force`를 직접 실행할 수 있습니다.");
        return 5;
    }

    private static int SelfTest()
    {
        SelfTests.Run();
        Console.WriteLine("자체 테스트를 모두 통과했습니다.");
        return 0;
    }

    private void EnsureFileSwitchingSupported()
    {
        var status = _authStorageInspector.Inspect(_paths);
        if (!status.SupportsOpaqueFileSwitching)
        {
            throw new InvalidOperationException(status.Description);
        }
    }

    private static void EnsureSingleArgument(string[] args, string usage)
    {
        if (args.Length != 2)
        {
            throw new ArgumentException($"사용법: {usage}");
        }
    }

    private static string DescribeInstallation(CodexInstallation installation)
    {
        return installation.Kind switch
        {
            CodexInstallKind.MicrosoftStore => $"Microsoft Store {installation.Version ?? "(버전 확인 불가)"}",
            CodexInstallKind.Standalone => "독립 실행형(이번 MVP 검증 범위 밖)",
            _ => "찾지 못함"
        };
    }

    private static void PrintSnapshotComparison(SnapshotComparison comparison)
    {
        if (comparison.IsFirstSnapshot)
        {
            Console.WriteLine("- 파일 변경 기준점: 생성됨");
            return;
        }

        Console.WriteLine(
            $"- 파일 변경: 추가 {comparison.Added.Count}, 제거 {comparison.Removed.Count}, 수정 {comparison.Changed.Count}");

        foreach (var path in comparison.Added.Take(10))
        {
            Console.WriteLine($"  + {path}");
        }

        foreach (var path in comparison.Removed.Take(10))
        {
            Console.WriteLine($"  - {path}");
        }

        foreach (var path in comparison.Changed.Take(10))
        {
            Console.WriteLine($"  * {path}");
        }
    }

    private static int Help()
    {
        PrintHelp();
        return 0;
    }

    private static int UnknownCommand(string command)
    {
        WriteError($"알 수 없는 명령입니다: {command}");
        PrintHelp();
        return 2;
    }

    private static void PrintHelp()
    {
        Console.WriteLine(
            """
            CodexSwitcher.Feasibility 명령

              inspect                    설치·인증 저장·프로세스·파일 변경 조사
              capture <slot>             현재 인증 상태를 DPAPI로 암호화해 슬롯에 저장
              prepare-login              최초 상태를 보관하고 새 로그인 준비 후 Codex 실행
              activate <slot>            슬롯 인증을 원자적으로 적용하고 Codex 실행
              restore-original           실험 전 최초 인증 상태 복구
              verify                     현재 자동 검증 상태 출력
              rollback-test              손상 슬롯 실패 주입과 직전 인증 복구 검증
              launch                     Store AUMID로 Codex 실행 요청
              close                      정상 종료 요청 후 최대 10초 대기
              close --force              사용자의 명시적 승인으로 Codex 강제 종료
              self-test                  실제 인증을 사용하지 않는 자체 테스트

            슬롯 이름은 영문, 숫자, 하이픈, 밑줄만 사용할 수 있습니다.
            인증 변경 명령은 Codex 프로세스가 하나라도 실행 중이면 중단됩니다.
            이 도구는 강제 종료를 수행하지 않습니다.
            """);
    }

    private static void WriteError(string message)
    {
        Console.Error.WriteLine($"오류: {message}");
    }
}
