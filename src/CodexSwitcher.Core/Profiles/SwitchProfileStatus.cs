namespace CodexSwitcher.Core.Profiles;

public enum SwitchProfileStatus
{
    Switched,
    AlreadyRunningTarget,
    ProfileNotFound,
    RunningUnknownProfile,
    InstallationNotFound,
    LaunchFailed,
    AuthenticationMismatch,
    RecoveryRequired,
    Failed
}
