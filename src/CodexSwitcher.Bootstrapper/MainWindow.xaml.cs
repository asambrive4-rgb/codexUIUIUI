using System.Windows;
using System.Windows.Controls;
using CodexSwitcher.Bootstrapper.Presentation;
using CodexSwitcher.Core.Profiles;

namespace CodexSwitcher.Bootstrapper;

public partial class MainWindow : Window
{
    private readonly MainWindowViewModel _viewModel;
    private readonly CreateProfileLoginUseCase _profileLogin;
    private CancellationTokenSource? _monitorCancellation;

    public MainWindow(
        MainWindowViewModel viewModel,
        CreateProfileLoginUseCase profileLogin)
    {
        InitializeComponent();
        _viewModel = viewModel;
        _profileLogin = profileLogin;
        DataContext = viewModel;
    }

    public void StartRuntimeMonitoring()
    {
        if (_monitorCancellation is not null)
        {
            return;
        }

        _monitorCancellation = new CancellationTokenSource();
        _ = MonitorRuntimeAsync(_monitorCancellation.Token);
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

        if (!profile.IsSwitchAction)
        {
            await _viewModel.RunProfileAsync(
                profile.Id,
                CancellationToken.None);
            return;
        }

        var result = await _viewModel.SwitchProfileAsync(
            profile.Id,
            forceCloseApproved: false,
            CancellationToken.None);
        if (result.Status !=
            SwitchProfileStatus.ForceCloseConfirmationRequired)
        {
            return;
        }

        if (!ConfirmForceCloseForSwitch())
        {
            return;
        }

        await _viewModel.SwitchProfileAsync(
            profile.Id,
            forceCloseApproved: true,
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
                MainWindowViewModel.DescribeDeleteResult(
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

        var result = await _viewModel.DeleteProfileAsync(
            profile.Id,
            CancellationToken.None);
        if (result.Status == DeleteProfileStatus.Deleted)
        {
            return;
        }

        MessageBox.Show(
            this,
            MainWindowViewModel.DescribeDeleteResult(result.Status),
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

    private bool ConfirmForceCloseForSwitch()
    {
        return MessageBox.Show(
                   this,
                   "Codex가 정상적으로 종료되지 않았습니다. 저장되지 않은 작업이 손실될 수 있습니다.\n\n강제 종료 후 전환하시겠습니까?",
                   "강제 종료 후 전환",
                   MessageBoxButton.YesNo,
                   MessageBoxImage.Warning,
                   MessageBoxResult.No) == MessageBoxResult.Yes;
    }

    protected override void OnClosed(EventArgs e)
    {
        _monitorCancellation?.Cancel();
        _monitorCancellation?.Dispose();
        _monitorCancellation = null;
        base.OnClosed(e);
    }

    private async Task MonitorRuntimeAsync(
        CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        try
        {
            while (await timer.WaitForNextTickAsync(
                       cancellationToken))
            {
                await _viewModel.RefreshRuntimeStateAsync(
                    cancellationToken);
            }
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            // 창이 닫히면 상태 확인을 끝낸다.
        }
    }
}
