using System.Windows.Media;
using CodexSwitcher.Core.Usage;

namespace CodexSwitcher.Bootstrapper.Presentation;

public sealed class RateLimitWindowViewModel : ObservableObject
{
    private static readonly Brush HealthyBrush =
        CreateBrush(0x2A, 0x8C, 0x83);
    private static readonly Brush WarningBrush =
        CreateBrush(0xD9, 0x9A, 0x00);
    private static readonly Brush DangerBrush =
        CreateBrush(0xC5, 0x3B, 0x3B);
    private static readonly Brush DeadBrush =
        CreateBrush(0x8A, 0x94, 0x97);

    private string _displayText;
    private string _automationName;
    private int _remainingPercent;
    private Brush _progressBrush = DeadBrush;

    public RateLimitWindowViewModel(string label)
    {
        Label = label;
        _displayText = $"{label} · 정보 없음";
        _automationName = _displayText;
    }

    public string Label { get; }

    public string DisplayText
    {
        get => _displayText;
        private set => SetField(ref _displayText, value);
    }

    public string AutomationName
    {
        get => _automationName;
        private set => SetField(ref _automationName, value);
    }

    public int RemainingPercent
    {
        get => _remainingPercent;
        private set => SetField(ref _remainingPercent, value);
    }

    public Brush ProgressBrush
    {
        get => _progressBrush;
        private set => SetField(ref _progressBrush, value);
    }

    public void Apply(RateLimitWindow? window)
    {
        if (window is null)
        {
            RemainingPercent = 0;
            ProgressBrush = DeadBrush;
            DisplayText = $"{Label} · 정보 없음";
            AutomationName = DisplayText;
            return;
        }

        RemainingPercent = window.RemainingPercent;
        ProgressBrush = window.DisplayLevel switch
        {
            RateLimitDisplayLevel.Healthy => HealthyBrush,
            RateLimitDisplayLevel.Warning => WarningBrush,
            RateLimitDisplayLevel.Danger => DangerBrush,
            _ => DeadBrush
        };

        var resetText = DescribeReset(window.ResetsAt);
        if (RemainingPercent == 0)
        {
            DisplayText = $"{Label} · Dead · {resetText}";
            AutomationName =
                $"{Label} · 현재 사용 한도 소진 · {resetText}";
            return;
        }

        DisplayText =
            $"{Label} · {RemainingPercent}% 남음 · {resetText}";
        AutomationName = DisplayText;
    }

    private static string DescribeReset(DateTimeOffset? resetsAt)
    {
        if (resetsAt is null)
        {
            return "초기화 시각 정보 없음";
        }

        var remaining = resetsAt.Value - DateTimeOffset.Now;
        if (remaining <= TimeSpan.Zero)
        {
            return "곧 초기화";
        }

        if (remaining < TimeSpan.FromDays(1))
        {
            var hours = (int)remaining.TotalHours;
            var minutes = remaining.Minutes;
            return hours > 0
                ? $"{hours}시간 {minutes}분 후 초기화"
                : $"{Math.Max(1, minutes)}분 후 초기화";
        }

        return $"{resetsAt.Value.ToLocalTime():M월 d일 HH:mm} 초기화";
    }

    private static Brush CreateBrush(
        byte red,
        byte green,
        byte blue)
    {
        var brush = new SolidColorBrush(
            Color.FromRgb(red, green, blue));
        brush.Freeze();
        return brush;
    }
}
