using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using CodexSwitcher.Bootstrapper.Presentation;
using CodexSwitcher.Bootstrapper.Usage;
using CodexSwitcher.Core.Profiles;
using CodexSwitcher.Core.Usage;
using FluentWindow = Wpf.Ui.Controls.FluentWindow;

namespace CodexSwitcher.Bootstrapper;

public partial class MainWindow : FluentWindow
{
    private readonly MainWindowViewModel _viewModel;
    private readonly CreateProfileLoginUseCase _profileLogin;
    private readonly ProfileUsageMonitor _usageMonitor;
    private readonly PopupPlacementStore _popupPlacementStore;
    private readonly object _usageSnapshotGate = new();
    private readonly Dictionary<ProfileId, ProfileRateLimitSnapshot>
        _pendingUsageSnapshots = [];
    private readonly object _runtimeStateGate = new();
    private ProfileRuntimeState? _pendingRuntimeState;
    private IDisposable? _usageSurfaceLease;
    private ProfilePopupWindow? _profilePopupWindow;
    private bool _usageMonitoringStarted;
    private bool _allowApplicationExit;
    private bool _usageSnapshotDrainScheduled;
    private bool _runtimeStateDrainScheduled;

    private bool HasVisibleSurface =>
        IsVisible ||
        _profilePopupWindow?.IsVisible == true;

    public MainWindow(
        MainWindowViewModel viewModel,
        CreateProfileLoginUseCase profileLogin,
        ProfileUsageMonitor usageMonitor,
        PopupPlacementStore popupPlacementStore)
    {
        InitializeComponent();
        _viewModel = viewModel;
        _profileLogin = profileLogin;
        _usageMonitor = usageMonitor;
        _popupPlacementStore = popupPlacementStore;
        DataContext = viewModel;
        IsVisibleChanged += MainWindow_IsVisibleChanged;
    }

    /// <summary>
    /// 런타임 폴링은 사용량 모니터 단일 루프에 통합됐다.
    /// 호환을 위해 남겨 두며, 사용량 모니터링 시작과 동일하게 동작한다.
    /// </summary>
    public void StartRuntimeMonitoring() =>
        StartUsageMonitoring();

    public void StartUsageMonitoring()
    {
        if (_usageMonitoringStarted)
        {
            return;
        }

        _usageMonitoringStarted = true;
        _usageMonitor.SnapshotChanged += OnUsageSnapshotChanged;
        _usageMonitor.RuntimeStateChanged += OnRuntimeStateChanged;
        _usageMonitor.RefreshingChanged += OnUsageRefreshingChanged;
        UpdateVisibleSurfaceRegistrations();
    }

    public bool IsOperationInProgress =>
        _viewModel.IsOperationInProgress;

    public void ReturnToMainWindow()
    {
        Show();
        RestoreAndActivate();
        CloseProfilePopup();
        UpdateVisibleSurfaceRegistrations();
    }

    public void ShowDefaultSurface()
    {
        if (_viewModel.DefaultPopupProfile is null)
        {
            Show();
            RestoreAndActivate();
            UpdateVisibleSurfaceRegistrations();
            return;
        }

        OpenProfilePopup(_viewModel.DefaultPopupProfile);
    }

    public void RestoreCompactSurface()
    {
        if (_profilePopupWindow?.IsVisible == true)
        {
            _profilePopupWindow.RestoreAndActivate();
            return;
        }

        if (!IsVisible &&
            _viewModel.DefaultPopupProfile is not null)
        {
            OpenProfilePopup(_viewModel.DefaultPopupProfile);
            return;
        }

        ReturnToMainWindow();
    }

    public void AllowApplicationExit()
    {
        _allowApplicationExit = true;
    }

    private async void AddProfile_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (_viewModel.HasPendingRecovery)
        {
            MessageBox.Show(
                this,
                "먼저 이전 로그인 작업을 복구하세요.",
                "복구 필요",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        using var pause = await _usageMonitor.PauseAsync(
            CancellationToken.None);
        var window = new NewProfileWindow(_profileLogin)
        {
            Owner = this
        };

        if (window.ShowDialog() == true)
        {
            await _viewModel.RefreshProfilesAsync(
                CancellationToken.None);
        }
    }

    private async void RunProfile_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (sender is not FrameworkElement
            {
                DataContext: ProfileListItemViewModel profile
            })
        {
            return;
        }

        using var pause = await _usageMonitor.PauseAsync(
            CancellationToken.None);
        if (!profile.IsSwitchAction)
        {
            await _viewModel.RunProfileAsync(
                profile.Id,
                CancellationToken.None);
            return;
        }

        await _viewModel.SwitchProfileAsync(
            profile.Id,
            CancellationToken.None);
    }

    private async void DeleteProfile_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (sender is not MenuItem
            {
                CommandParameter: ProfileListItemViewModel profile
            })
        {
            return;
        }

        if (profile.IsActive)
        {
            MessageBox.Show(
                this,
                ProfileOperationMessageFormatter.Describe(
                    DeleteProfileStatus.ActiveProfileBlocked),
                "프로필 삭제",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        var confirmation = new DeleteProfileConfirmationWindow(
            profile.Name)
        {
            Owner = this
        };

        if (confirmation.ShowDialog() != true)
        {
            return;
        }

        using var pause = await _usageMonitor.PauseAsync(
            CancellationToken.None);
        var result = await _viewModel.DeleteProfileAsync(
            profile.Id,
            CancellationToken.None);
        if (result.Status == DeleteProfileStatus.Deleted)
        {
            return;
        }

        MessageBox.Show(
            this,
            ProfileOperationMessageFormatter.Describe(result.Status),
            "프로필 삭제",
            MessageBoxButton.OK,
            result.Status == DeleteProfileStatus.Failed
                ? MessageBoxImage.Error
                : MessageBoxImage.Warning);
    }

    private async void RecoverPreviousLogin_Click(
        object sender,
        RoutedEventArgs e)
    {
        RecoveryButton.IsEnabled = false;
        try
        {
            using var pause = await _usageMonitor.PauseAsync(
                CancellationToken.None);
            var result = await _profileLogin.CancelAsync(
                forceCloseApproved: false,
                CancellationToken.None);

            if (result.Status ==
                CreateProfileLoginStatus.ForceCloseConfirmationRequired)
            {
                if (!ConfirmForceClose())
                {
                    return;
                }

                result = await _profileLogin.CancelAsync(
                    forceCloseApproved: true,
                    CancellationToken.None);
            }

            if (result.Status == CreateProfileLoginStatus.Canceled)
            {
                await _viewModel.RefreshProfilesAsync(
                    CancellationToken.None);
                MessageBox.Show(
                    this,
                    "이전 인증 상태를 복구했습니다.",
                    "복구 완료",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            MessageBox.Show(
                this,
                "이전 인증 상태를 복구하지 못했습니다. Codex가 완전히 종료됐는지 확인한 뒤 다시 시도하세요.",
                "복구 실패",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            RecoveryButton.IsEnabled = true;
        }
    }

    private async void UsageRefresh_Click(
        object sender,
        RoutedEventArgs e)
    {
        await _usageMonitor.RefreshAllNowAsync(
            CancellationToken.None);
    }

    private void OpenProfilePopup_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (sender is not FrameworkElement
            {
                DataContext: ProfileListItemViewModel profile
            })
        {
            return;
        }

        OpenProfilePopup(profile);
    }

    private void ManageButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (sender is FrameworkElement element)
        {
            if (element.ContextMenu != null)
            {
                element.ContextMenu.PlacementTarget = element;
                element.ContextMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
                element.ContextMenu.IsOpen = true;
            }
        }
    }

    private bool ConfirmForceClose()
    {
        return MessageBox.Show(
                   this,
                   "Codex가 정상적으로 종료되지 않았습니다. 저장되지 않은 작업이 손실될 수 있습니다.\n\n강제 종료 후 계속하시겠습니까?",
                   "강제 종료 확인",
                   MessageBoxButton.YesNo,
                   MessageBoxImage.Warning,
                   MessageBoxResult.No) == MessageBoxResult.Yes;
    }

    protected override void OnClosed(EventArgs e)
    {
        IsVisibleChanged -= MainWindow_IsVisibleChanged;
        _usageMonitor.SnapshotChanged -= OnUsageSnapshotChanged;
        _usageMonitor.RuntimeStateChanged -= OnRuntimeStateChanged;
        _usageMonitor.RefreshingChanged -= OnUsageRefreshingChanged;
        DisposeProfilePopup();
        _usageSurfaceLease?.Dispose();
        _usageSurfaceLease = null;
        lock (_usageSnapshotGate)
        {
            _pendingUsageSnapshots.Clear();
            _usageSnapshotDrainScheduled = false;
        }

        lock (_runtimeStateGate)
        {
            _pendingRuntimeState = null;
            _runtimeStateDrainScheduled = false;
        }

        base.OnClosed(e);
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_allowApplicationExit)
        {
            e.Cancel = true;
            Hide();
        }

        base.OnClosing(e);
    }

    private void MainWindow_IsVisibleChanged(
        object sender,
        DependencyPropertyChangedEventArgs e)
    {
        UpdateVisibleSurfaceRegistrations();
    }

    private void UpdateVisibleSurfaceRegistrations()
    {
        if (!_usageMonitoringStarted)
        {
            return;
        }

        if (HasVisibleSurface)
        {
            _usageSurfaceLease ??=
                _usageMonitor.AcquireVisibleSurface();
            return;
        }

        _usageSurfaceLease?.Dispose();
        _usageSurfaceLease = null;
    }

    private void OnUsageSnapshotChanged(
        object? sender,
        ProfileRateLimitSnapshot snapshot)
    {
        lock (_usageSnapshotGate)
        {
            _pendingUsageSnapshots[snapshot.ProfileId] = snapshot;
            if (_usageSnapshotDrainScheduled)
            {
                return;
            }

            _usageSnapshotDrainScheduled = true;
        }

        _ = Dispatcher.InvokeAsync(
            DrainUsageSnapshots,
            DispatcherPriority.Background);
    }

    private void DrainUsageSnapshots()
    {
        ProfileRateLimitSnapshot[] snapshots;
        lock (_usageSnapshotGate)
        {
            snapshots = _pendingUsageSnapshots.Values.ToArray();
            _pendingUsageSnapshots.Clear();
            _usageSnapshotDrainScheduled = false;
        }

        foreach (var snapshot in snapshots)
        {
            _viewModel.ApplyUsageSnapshot(snapshot);
        }
    }

    private void OnRuntimeStateChanged(
        object? sender,
        ProfileRuntimeState state)
    {
        lock (_runtimeStateGate)
        {
            _pendingRuntimeState = state;
            if (_runtimeStateDrainScheduled)
            {
                return;
            }

            _runtimeStateDrainScheduled = true;
        }

        _ = Dispatcher.InvokeAsync(
            DrainRuntimeState,
            DispatcherPriority.Background);
    }

    private async void DrainRuntimeState()
    {
        ProfileRuntimeState? state;
        lock (_runtimeStateGate)
        {
            state = _pendingRuntimeState;
            _pendingRuntimeState = null;
            _runtimeStateDrainScheduled = false;
        }

        if (state is null)
        {
            return;
        }

        try
        {
            await _viewModel.ApplyMonitoredRuntimeStateAsync(
                state,
                CancellationToken.None);
        }
        catch (OperationCanceledException)
        {
            // 창이 닫히는 중이면 무시한다.
        }
    }

    private void OnUsageRefreshingChanged(
        object? sender,
        bool isRefreshing)
    {
        _ = Dispatcher.InvokeAsync(
            () => _viewModel.SetUsageRefreshing(isRefreshing));
    }

    private void ProfilePopupWindow_ReturnRequested(
        object? sender,
        EventArgs e) =>
        ReturnToMainWindow();

    private void ProfilePopupWindow_MinimizeRequested(
        object? sender,
        EventArgs e)
    {
        HideProfilePopup();
        Hide();
        UpdateVisibleSurfaceRegistrations();
    }

    private void ProfilePopupWindow_Closed(
        object? sender,
        EventArgs e)
    {
        if (sender is not ProfilePopupWindow popup ||
            popup != _profilePopupWindow)
        {
            return;
        }

        popup.ReturnRequested -= ProfilePopupWindow_ReturnRequested;
        popup.MinimizeRequested -= ProfilePopupWindow_MinimizeRequested;
        popup.Closed -= ProfilePopupWindow_Closed;
        _profilePopupWindow = null;
        UpdateVisibleSurfaceRegistrations();
    }

    /// <summary>
    /// 팝업을 숨긴다. 인스턴스는 재사용을 위해 유지한다.
    /// </summary>
    private void CloseProfilePopup() =>
        HideProfilePopup();

    private void HideProfilePopup()
    {
        _profilePopupWindow?.HidePopup();
    }

    private void DisposeProfilePopup()
    {
        if (_profilePopupWindow is null)
        {
            return;
        }

        var popup = _profilePopupWindow;
        popup.ReturnRequested -= ProfilePopupWindow_ReturnRequested;
        popup.MinimizeRequested -= ProfilePopupWindow_MinimizeRequested;
        popup.Closed -= ProfilePopupWindow_Closed;
        _profilePopupWindow = null;
        popup.Close();
    }

    private void OpenProfilePopup(ProfileListItemViewModel profile)
    {
        EnsureProfilePopup();
        _profilePopupWindow!.ShowProfile(profile);
        Hide();
        UpdateVisibleSurfaceRegistrations();
    }

    private void EnsureProfilePopup()
    {
        if (_profilePopupWindow is not null)
        {
            return;
        }

        var popup = new ProfilePopupWindow(_popupPlacementStore);
        popup.ReturnRequested += ProfilePopupWindow_ReturnRequested;
        popup.MinimizeRequested += ProfilePopupWindow_MinimizeRequested;
        popup.Closed += ProfilePopupWindow_Closed;
        _profilePopupWindow = popup;
    }

    private void RestoreAndActivate()
    {
        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
        }

        if (IsActive)
        {
            _ = Focus();
            return;
        }

        Topmost = true;
        Topmost = false;
        _ = Activate();
        _ = Focus();
    }
}
