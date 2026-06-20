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
    private string _resetTimeText = "정보 없음";
    private bool _isDataAvailable;

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

    public string ResetTimeText
    {
        get => _resetTimeText;
        private set => SetField(ref _resetTimeText, value);
    }

    public bool IsDataAvailable
    {
        get => _isDataAvailable;
        private set => SetField(ref _isDataAvailable, value);
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
            AutomationName = $"{Label} · 정보 없음";
            ResetTimeText = "정보 없음";
            IsDataAvailable = false;
            return;
        }

        RemainingPercent = window.RemainingPercent;
        IsDataAvailable = true;
        ProgressBrush = window.DisplayLevel switch
        {
            RateLimitDisplayLevel.Healthy => HealthyBrush,
            RateLimitDisplayLevel.Warning => WarningBrush,
            RateLimitDisplayLevel.Danger => DangerBrush,
            _ => DeadBrush
        };

        var resetText = DescribeReset(window.ResetsAt);
        ResetTimeText = resetText;

        if (RemainingPercent == 0)
        {
            DisplayText = $"{Label} · 0% · {resetText}";
            AutomationName = $"{Label} · 현재 사용 한도 소진 · {resetText}";
            return;
        }

        DisplayText = $"{Label} · {RemainingPercent}% · {resetText}";
        AutomationName = $"{Label} · {RemainingPercent}% 남음 · 초기화: {resetText}";
    }

    private string DescribeReset(DateTimeOffset? resetsAt)
    {
        if (resetsAt is null)
        {
            return "정보 없음";
        }

        var remaining = resetsAt.Value - DateTimeOffset.Now;
        if (remaining <= TimeSpan.Zero)
        {
            return "곧 초기화";
        }

        var localTime = resetsAt.Value.ToLocalTime();
        if (Label == "5시간 한도")
        {
            return localTime.ToString("HH:mm");
        }
        else
        {
            return localTime.ToString("M월 d일");
        }
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
