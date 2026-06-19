using CodexSwitcher.Core.Profiles;
using CodexSwitcher.Core.Usage;

namespace CodexSwitcher.Bootstrapper.Presentation;

public sealed class ProfileListItemViewModel : ObservableObject
{
    private string _status = "준비됨";
    private string _buttonText = "실행";
    private bool _isRunEnabled = true;
    private bool _isDeleteEnabled = true;
    private bool _isSwitchAction;
    private bool _isActive;
    private string _usageStatusMessage = "사용량 확인 중...";
    private bool _hasUsageData;

    public ProfileListItemViewModel(
        ProfileId id,
        string name)
    {
        Id = id;
        Name = name;
    }

    public ProfileId Id { get; }

    public string Name { get; }

    public string Status
    {
        get => _status;
        set => SetField(ref _status, value);
    }

    public bool IsRunEnabled
    {
        get => _isRunEnabled;
        set => SetField(ref _isRunEnabled, value);
    }

    public bool IsDeleteEnabled
    {
        get => _isDeleteEnabled;
        set => SetField(ref _isDeleteEnabled, value);
    }

    public string ButtonText
    {
        get => _buttonText;
        set => SetField(ref _buttonText, value);
    }

    public bool IsSwitchAction
    {
        get => _isSwitchAction;
        set => SetField(ref _isSwitchAction, value);
    }

    public bool IsActive
    {
        get => _isActive;
        set => SetField(ref _isActive, value);
    }

    public RateLimitWindowViewModel FiveHourUsage { get; } =
        new("5시간 한도");

    public RateLimitWindowViewModel WeeklyUsage { get; } =
        new("주간 한도");

    public string UsageStatusMessage
    {
        get => _usageStatusMessage;
        private set
        {
            if (SetField(ref _usageStatusMessage, value))
            {
                OnPropertyChanged(nameof(HasUsageStatus));
            }
        }
    }

    public bool HasUsageData
    {
        get => _hasUsageData;
        private set => SetField(ref _hasUsageData, value);
    }

    public bool HasUsageStatus =>
        !string.IsNullOrWhiteSpace(UsageStatusMessage);

    public void ApplyUsageSnapshot(
        ProfileRateLimitSnapshot snapshot)
    {
        FiveHourUsage.Apply(snapshot.FiveHourLimit);
        WeeklyUsage.Apply(snapshot.WeeklyLimit);
        HasUsageData =
            snapshot.FiveHourLimit is not null ||
            snapshot.WeeklyLimit is not null;

        UsageStatusMessage = snapshot.Status switch
        {
            ProfileRateLimitStatus.Loading =>
                HasUsageData
                    ? "갱신 중..."
                    : "사용량 확인 중...",
            ProfileRateLimitStatus.Available => "",
            ProfileRateLimitStatus.UnsupportedAuthentication =>
                "ChatGPT 계정 사용량만 확인할 수 있습니다.",
            ProfileRateLimitStatus.AuthenticationExpired =>
                "인증이 만료되어 사용량을 확인할 수 없습니다.",
            ProfileRateLimitStatus.CodexUpdateRequired =>
                "Codex 업데이트 후 사용량을 확인할 수 있습니다.",
            _ when HasUsageData =>
                $"갱신 실패 · 마지막 확인 {DescribeLastSuccess(snapshot.LastSuccessfulAt)}",
            _ => "사용량을 불러오지 못했습니다."
        };
    }

    private static string DescribeLastSuccess(
        DateTimeOffset? lastSuccessfulAt)
    {
        if (lastSuccessfulAt is null)
        {
            return "시각 정보 없음";
        }

        var elapsed = DateTimeOffset.Now - lastSuccessfulAt.Value;
        if (elapsed < TimeSpan.FromMinutes(1))
        {
            return "방금 전";
        }

        if (elapsed < TimeSpan.FromHours(1))
        {
            return $"{(int)elapsed.TotalMinutes}분 전";
        }

        return $"{(int)elapsed.TotalHours}시간 전";
    }
}
