namespace CodexSwitcher.Core.Profiles;

public enum SwitchProfileStatus
{
    Switched,
    AlreadyRunningTarget,
    ProfileNotFound,
    RunningUnknownProfile,
    ForceCloseConfirmationRequired,
    InstallationNotFound,
    LaunchFailed,
    AuthenticationMismatch,
    RecoveryRequired,
    Failed
}
