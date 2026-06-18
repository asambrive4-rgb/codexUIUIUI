namespace CodexSwitcher.Core.Profiles;

public enum CreateProfileLoginStatus
{
    WaitingForLogin,
    Created,
    Canceled,
    InvalidName,
    DuplicateName,
    StorageNeedsAttention,
    ForceCloseConfirmationRequired,
    LoginNotCompleted,
    InstallationNotFound,
    RecoveryRequired,
    Failed
}
