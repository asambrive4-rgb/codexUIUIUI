namespace CodexSwitcher.Core.Profiles;

public enum RunProfileStatus
{
    Running,
    AlreadyRunning,
    ProfileNotFound,
    InstallationNotFound,
    LaunchFailed,
    AuthenticationMismatch,
    RecoveryRequired,
    Failed
}
