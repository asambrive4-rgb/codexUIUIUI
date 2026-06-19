using System.Collections.ObjectModel;
using CodexSwitcher.Core.Profiles;
using CodexSwitcher.Core.Usage;

namespace CodexSwitcher.Bootstrapper.Presentation;

internal sealed class ProfileListPresentationState
{
    public ObservableCollection<ProfileListItemViewModel> Profiles { get; } =
        [];

    public void Replace(IReadOnlyList<Profile> profiles)
    {
        Profiles.Clear();
        foreach (var profile in profiles)
        {
            Profiles.Add(
                new ProfileListItemViewModel(
                    profile.Id,
                    profile.Name.Value));
        }
    }

    public void BeginOperation(
        ProfileId profileId,
        string status)
    {
        var target = Profiles.FirstOrDefault(
            profile => profile.Id == profileId);
        if (target is not null)
        {
            target.Status = status;
        }

        DisableAllActions();
    }

    public void ApplyUsageSnapshot(
        ProfileRateLimitSnapshot snapshot)
    {
        Profiles.FirstOrDefault(
                profile => profile.Id == snapshot.ProfileId)
            ?.ApplyUsageSnapshot(snapshot);
    }

    public string ApplyRuntimeState(
        ProfileRuntimeState state,
        bool canOperate)
    {
        return state.Status switch
        {
            ProfileRuntimeStatus.Stopped =>
                ApplyStopped(canOperate),
            ProfileRuntimeStatus.RunningKnownProfile =>
                ApplyKnownProfile(state.ActiveProfileId, canOperate),
            _ => ApplyUnknownProfile(canOperate)
        };
    }

    public void DisableAllActions()
    {
        foreach (var profile in Profiles)
        {
            profile.IsRunEnabled = false;
            profile.IsDeleteEnabled = false;
        }
    }

    private string ApplyStopped(bool canOperate)
    {
        foreach (var profile in Profiles)
        {
            profile.IsActive = false;
            profile.Status = "준비됨";
            profile.ButtonText = "실행";
            profile.IsSwitchAction = false;
            profile.IsRunEnabled = canOperate;
            profile.IsDeleteEnabled = canOperate;
        }

        return "Codex 종료됨";
    }

    private string ApplyKnownProfile(
        ProfileId? activeProfileId,
        bool canOperate)
    {
        var active = Profiles.FirstOrDefault(
            profile => profile.Id == activeProfileId);
        foreach (var profile in Profiles)
        {
            profile.IsActive =
                active is not null &&
                profile.Id == active.Id;
            profile.Status = profile.IsActive
                ? "실행 중"
                : "준비됨";
            profile.ButtonText = profile.IsActive
                ? "실행 중"
                : "전환";
            profile.IsSwitchAction =
                active is not null &&
                !profile.IsActive;
            profile.IsRunEnabled =
                !profile.IsActive &&
                active is not null &&
                canOperate;
            profile.IsDeleteEnabled = canOperate;
        }

        return active is null
            ? "Codex 실행 중 · 프로필 확인 불가"
            : $"Codex 실행 중 · {active.Name}";
    }

    private string ApplyUnknownProfile(bool canOperate)
    {
        foreach (var profile in Profiles)
        {
            profile.IsActive = false;
            profile.Status = "준비됨";
            profile.ButtonText = "전환";
            profile.IsSwitchAction = true;
            profile.IsRunEnabled = false;
            profile.IsDeleteEnabled = canOperate;
        }

        return "Codex 실행 중 · 프로필 확인 불가";
    }
}
