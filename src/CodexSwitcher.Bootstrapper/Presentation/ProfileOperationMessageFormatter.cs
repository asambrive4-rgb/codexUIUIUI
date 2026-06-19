using CodexSwitcher.Core.Profiles;

namespace CodexSwitcher.Bootstrapper.Presentation;

internal static class ProfileOperationMessageFormatter
{
    public static string Describe(RunProfileStatus status) =>
        status switch
        {
            RunProfileStatus.Running =>
                "Codex를 실행했습니다.",
            RunProfileStatus.AlreadyRunning =>
                "이미 Codex가 실행 중입니다.",
            RunProfileStatus.ProfileNotFound =>
                "선택한 프로필을 찾을 수 없습니다.",
            RunProfileStatus.InstallationNotFound =>
                "실행할 수 있는 Codex 설치를 찾지 못했습니다.",
            RunProfileStatus.LaunchFailed =>
                "Codex 실행에 실패했습니다. 이전 인증 상태를 복구했습니다.",
            RunProfileStatus.AuthenticationMismatch =>
                "인증 상태 확인에 실패해 이전 상태를 복구했습니다.",
            RunProfileStatus.RecoveryRequired =>
                "인증 복구가 필요합니다. 이전 로그인 작업 복구를 실행하세요.",
            _ => "프로필 실행 중 오류가 발생했습니다."
        };

    public static string Describe(SwitchProfileStatus status) =>
        status switch
        {
            SwitchProfileStatus.Switched =>
                "프로필을 전환했습니다.",
            SwitchProfileStatus.AlreadyRunningTarget =>
                "이미 선택한 프로필로 실행 중입니다.",
            SwitchProfileStatus.ProfileNotFound =>
                "선택한 프로필을 찾을 수 없습니다.",
            SwitchProfileStatus.RunningUnknownProfile =>
                "현재 실행 중인 Codex 프로필을 확인할 수 없어 전환하지 않았습니다.",
            SwitchProfileStatus.InstallationNotFound =>
                "실행할 수 있는 Codex 설치를 찾지 못했습니다.",
            SwitchProfileStatus.LaunchFailed =>
                "Codex 재실행에 실패했습니다. 이전 인증 상태를 복구했습니다.",
            SwitchProfileStatus.AuthenticationMismatch =>
                "전환 후 인증 상태 확인에 실패해 이전 상태를 복구했습니다.",
            SwitchProfileStatus.RecoveryRequired =>
                "인증 복구가 필요합니다. 이전 로그인 작업 복구를 실행하세요.",
            _ => "프로필 전환 중 오류가 발생했습니다."
        };

    public static string Describe(DeleteProfileStatus status) =>
        status switch
        {
            DeleteProfileStatus.Deleted =>
                "프로필을 삭제했습니다.",
            DeleteProfileStatus.ProfileNotFound =>
                "선택한 프로필을 찾을 수 없습니다.",
            DeleteProfileStatus.ActiveProfileBlocked =>
                "실행 중인 프로필은 바로 삭제할 수 없습니다. Codex를 종료하거나 다른 프로필로 전환한 뒤 다시 시도하세요.",
            DeleteProfileStatus.RunningProfileUnknown =>
                "현재 실행 중인 Codex 프로필을 확인할 수 없어 삭제하지 않았습니다. Codex를 종료한 뒤 다시 시도하세요.",
            DeleteProfileStatus.RecoveryRequired =>
                "인증 복구가 필요합니다. 이전 로그인 작업 복구를 실행하세요.",
            _ => "프로필 삭제에 실패했습니다. 목록은 변경하지 않았습니다."
        };
}
