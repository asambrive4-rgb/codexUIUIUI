using System.Windows.Media;
using CodexSwitcher.Bootstrapper.Presentation;
using CodexSwitcher.Core.Profiles;
using CodexSwitcher.Core.Usage;

namespace CodexSwitcher.Tests.Presentation;

[TestClass]
public sealed class ProfilePresentationTests
{
    [TestMethod]
    public void ApplyRuntimeState_Stopped_EnablesRunAndDelete()
    {
        var state = CreateState();

        var message = state.ApplyRuntimeState(
            new ProfileRuntimeState(ProfileRuntimeStatus.Stopped),
            canOperate: true);

        Assert.AreEqual("Codex 종료됨", message);
        Assert.IsTrue(state.Profiles.All(
            profile =>
                profile.Status == "준비됨" &&
                profile.ButtonText == "실행" &&
                profile.IsRunEnabled &&
                profile.IsDeleteEnabled &&
                !profile.IsSwitchAction &&
                !profile.IsActive));
    }

    [TestMethod]
    public void ApplyRuntimeState_KnownProfile_MarksOnlyActiveProfile()
    {
        var state = CreateState();
        var active = state.Profiles[0];

        var message = state.ApplyRuntimeState(
            new ProfileRuntimeState(
                ProfileRuntimeStatus.RunningKnownProfile,
                active.Id),
            canOperate: true);

        Assert.AreEqual($"Codex 실행 중 · {active.Name}", message);
        Assert.IsTrue(active.IsActive);
        Assert.AreEqual("실행 중", active.Status);
        Assert.AreEqual("실행 중", active.ButtonText);
        Assert.IsFalse(active.IsRunEnabled);
        Assert.IsTrue(active.IsDeleteEnabled);

        var inactive = state.Profiles[1];
        Assert.IsFalse(inactive.IsActive);
        Assert.AreEqual("전환", inactive.ButtonText);
        Assert.IsTrue(inactive.IsSwitchAction);
        Assert.IsTrue(inactive.IsRunEnabled);
        Assert.IsTrue(inactive.IsDeleteEnabled);
    }

    [TestMethod]
    public void ApplyRuntimeState_UnknownProfile_DisablesRun()
    {
        var state = CreateState();

        var message = state.ApplyRuntimeState(
            new ProfileRuntimeState(
                ProfileRuntimeStatus.RunningUnknownProfile),
            canOperate: false);

        Assert.AreEqual(
            "Codex 실행 중 · 프로필 확인 불가",
            message);
        Assert.IsTrue(state.Profiles.All(
            profile =>
                profile.ButtonText == "전환" &&
                profile.IsSwitchAction &&
                !profile.IsRunEnabled &&
                !profile.IsDeleteEnabled));
    }

    [TestMethod]
    public void BeginOperation_UpdatesTargetAndDisablesAllActions()
    {
        var state = CreateState();
        var target = state.Profiles[1];

        state.BeginOperation(target.Id, "전환 중...");

        Assert.AreEqual("전환 중...", target.Status);
        Assert.IsTrue(state.Profiles.All(
            profile =>
                !profile.IsRunEnabled &&
                !profile.IsDeleteEnabled));
    }

    [TestMethod]
    public void ApplyUsageSnapshot_UpdatesOnlyMatchingProfile()
    {
        var state = CreateState();
        var target = state.Profiles[0];

        state.ApplyUsageSnapshot(
            new ProfileRateLimitSnapshot(
                target.Id,
                ProfileRateLimitStatus.Available,
                new RateLimitWindow(25, 300, null),
                new RateLimitWindow(60, 10080, null)));

        Assert.IsTrue(target.HasUsageData);
        Assert.AreEqual(75, target.FiveHourUsage.RemainingPercent);
        Assert.AreEqual(40, target.WeeklyUsage.RemainingPercent);
        Assert.IsFalse(state.Profiles[1].HasUsageData);
    }

    [TestMethod]
    public void ApplyUsageSnapshot_Failed_PreservesLastUsageData()
    {
        var profile = new ProfileListItemViewModel(
            ProfileId.New(),
            "Work");
        profile.ApplyUsageSnapshot(
            new ProfileRateLimitSnapshot(
                profile.Id,
                ProfileRateLimitStatus.Available,
                new RateLimitWindow(20, 300, null)));

        profile.ApplyUsageSnapshot(
            new ProfileRateLimitSnapshot(
                profile.Id,
                ProfileRateLimitStatus.Failed,
                new RateLimitWindow(20, 300, null),
                LastSuccessfulAt: DateTimeOffset.Now));

        Assert.IsTrue(profile.HasUsageData);
        Assert.AreEqual(80, profile.FiveHourUsage.RemainingPercent);
        StringAssert.StartsWith(
            profile.UsageStatusMessage,
            "갱신 실패 · 마지막 확인");
    }

    [TestMethod]
    public void ApplyUsageSnapshot_AuthenticationExpired_ShowsStatus()
    {
        var profile = new ProfileListItemViewModel(
            ProfileId.New(),
            "Work");

        profile.ApplyUsageSnapshot(
            new ProfileRateLimitSnapshot(
                profile.Id,
                ProfileRateLimitStatus.AuthenticationExpired));

        Assert.IsFalse(profile.HasUsageData);
        Assert.AreEqual(
            "인증이 만료되어 사용량을 확인할 수 없습니다.",
            profile.UsageStatusMessage);
        Assert.IsTrue(profile.HasUsageStatus);
    }

    [TestMethod]
    [DataRow(50, 50, 0x2A, 0x8C, 0x83)]
    [DataRow(70, 30, 0xD9, 0x9A, 0x00)]
    [DataRow(90, 10, 0xC5, 0x3B, 0x3B)]
    [DataRow(100, 0, 0x8A, 0x94, 0x97)]
    public void RateLimitWindow_MapsLevelToDisplay(
        int usedPercent,
        int remainingPercent,
        int red,
        int green,
        int blue)
    {
        var viewModel = new RateLimitWindowViewModel("테스트");

        viewModel.Apply(
            new RateLimitWindow(
                usedPercent,
                300,
                DateTimeOffset.Now.AddHours(1)));

        Assert.AreEqual(remainingPercent, viewModel.RemainingPercent);
        var brush = (SolidColorBrush)viewModel.ProgressBrush;
        Assert.AreEqual(
            Color.FromRgb((byte)red, (byte)green, (byte)blue),
            brush.Color);
        if (remainingPercent == 0)
        {
            StringAssert.Contains(viewModel.DisplayText, "소진");
            StringAssert.Contains(
                viewModel.AutomationName,
                "현재 사용 한도 소진");
        }
    }

    [TestMethod]
    public void OperationFormatter_PreservesRepresentativeMessages()
    {
        Assert.AreEqual(
            "Codex를 실행했습니다.",
            ProfileOperationMessageFormatter.Describe(
                RunProfileStatus.Running));
        Assert.AreEqual(
            "현재 실행 중인 Codex 프로필을 확인할 수 없어 전환하지 않았습니다.",
            ProfileOperationMessageFormatter.Describe(
                SwitchProfileStatus.RunningUnknownProfile));
        Assert.AreEqual(
            "실행 중인 프로필은 바로 삭제할 수 없습니다. Codex를 종료하거나 다른 프로필로 전환한 뒤 다시 시도하세요.",
            ProfileOperationMessageFormatter.Describe(
                DeleteProfileStatus.ActiveProfileBlocked));
    }

    private static ProfileListPresentationState CreateState()
    {
        var state = new ProfileListPresentationState();
        state.Replace(
        [
            new Profile(
                ProfileId.New(),
                ProfileName.Create("Work")),
            new Profile(
                ProfileId.New(),
                ProfileName.Create("Personal"))
        ]);
        return state;
    }
}
