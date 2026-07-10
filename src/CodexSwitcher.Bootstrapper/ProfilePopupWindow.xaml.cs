using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using CodexSwitcher.Bootstrapper.Presentation;

namespace CodexSwitcher.Bootstrapper;

public partial class ProfilePopupWindow : Window
{
    private const double EdgeMargin = 4;
    private const double DefaultLowerOffset = 8;

    private readonly PopupPlacementStore _placementStore;
    private bool _hasPlaced;

    public ProfilePopupWindow(PopupPlacementStore placementStore)
    {
        InitializeComponent();
        _placementStore = placementStore;
    }

    public event EventHandler? ReturnRequested;

    public event EventHandler? MinimizeRequested;

    /// <summary>
    /// 같은 창 인스턴스를 재사용하며 프로필만 바인딩한다.
    /// </summary>
    public void ShowProfile(ProfileListItemViewModel profile)
    {
        DataContext = profile;

        if (!IsVisible)
        {
            Show();
        }

        if (_hasPlaced)
        {
            RestoreAndActivate();
            return;
        }

        if (IsLoaded)
        {
            PlacePopup();
            _hasPlaced = true;
            RestoreAndActivate();
        }
    }

    public void HidePopup()
    {
        if (IsVisible)
        {
            Hide();
        }
    }

    public void RestoreAndActivate()
    {
        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
        }

        Topmost = true;
        if (!IsActive)
        {
            _ = Activate();
        }

        _ = Focus();
    }

    protected override void OnContentRendered(EventArgs e)
    {
        base.OnContentRendered(e);
        if (!_hasPlaced)
        {
            PlacePopup();
            _hasPlaced = true;
        }

        RestoreAndActivate();
    }

    private void ReturnButton_Click(
        object sender,
        RoutedEventArgs e) =>
        ReturnRequested?.Invoke(this, EventArgs.Empty);

    private void MinimizeButton_Click(
        object sender,
        RoutedEventArgs e) =>
        MinimizeRequested?.Invoke(this, EventArgs.Empty);

    private void PopupSurface_MouseLeftButtonDown(
        object sender,
        MouseButtonEventArgs e)
    {
        if (e.ButtonState != MouseButtonState.Pressed ||
            IsInsideButton(e.OriginalSource as DependencyObject))
        {
            return;
        }

        try
        {
            DragMove();
            _ = _placementStore.SaveAsync(
                Left,
                Top,
                CancellationToken.None);
        }
        catch (InvalidOperationException)
        {
            // 마우스 버튼 상태가 DragMove 조건과 맞지 않으면 이동만 무시한다.
        }
    }

    private void PlacePopup()
    {
        var screen = System.Windows.Forms.Screen.FromPoint(
            System.Windows.Forms.Cursor.Position);
        var workingArea = screen.WorkingArea;
        var transform = PresentationSource.FromVisual(this)
            ?.CompositionTarget
            ?.TransformFromDevice ?? Matrix.Identity;

        var topLeft = transform.Transform(
            new Point(workingArea.Left, workingArea.Top));
        var bottomRight = transform.Transform(
            new Point(workingArea.Right, workingArea.Bottom));
        var popupWidth = ActualWidth > 0 ? ActualWidth : Width;
        var popupHeight = ActualHeight > 0 ? ActualHeight : Height;
        var saved = _placementStore.ReadCached();

        if (saved is not null)
        {
            Left = Clamp(
                saved.Left,
                topLeft.X + EdgeMargin,
                bottomRight.X - popupWidth - EdgeMargin);
            Top = Clamp(
                saved.Top,
                topLeft.Y + EdgeMargin,
                bottomRight.Y - popupHeight);
            return;
        }

        Left = topLeft.X + EdgeMargin;
        Top = Math.Min(
            bottomRight.Y - popupHeight,
            Math.Max(
                topLeft.Y + EdgeMargin,
                bottomRight.Y -
                popupHeight -
                EdgeMargin +
                DefaultLowerOffset));
    }

    private static bool IsInsideButton(DependencyObject? source)
    {
        while (source is not null)
        {
            if (source is Button)
            {
                return true;
            }

            source = VisualTreeHelper.GetParent(source);
        }

        return false;
    }

    private static double Clamp(
        double value,
        double minimum,
        double maximum)
    {
        if (maximum < minimum)
        {
            return minimum;
        }

        return Math.Min(
            Math.Max(value, minimum),
            maximum);
    }
}
