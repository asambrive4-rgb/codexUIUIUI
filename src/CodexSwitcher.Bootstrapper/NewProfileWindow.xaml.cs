using System.ComponentModel;
using System.Windows;
using CodexSwitcher.Core.Profiles;
using FluentWindow = Wpf.Ui.Controls.FluentWindow;

namespace CodexSwitcher.Bootstrapper;

public partial class NewProfileWindow : FluentWindow
{
    private readonly CreateProfileLoginUseCase _profileLogin;
    private bool _waitingForLogin;
    private bool _operationInProgress;
    private bool _allowClose;

    public NewProfileWindow(
        CreateProfileLoginUseCase profileLogin)
    {
        InitializeComponent();
        _profileLogin = profileLogin;
        Loaded += (_, _) => ProfileNameTextBox.Focus();
        Closing += OnClosing;
    }

    private async void Start_Click(
        object sender,
        RoutedEventArgs e)
    {
        await RunAsync(
            force => _profileLogin.StartAsync(
                ProfileNameTextBox.Text,
                force,
                CancellationToken.None));
    }

    private async void Complete_Click(
        object sender,
        RoutedEventArgs e)
    {
        await RunAsync(
            force => _profileLogin.CompleteAsync(
                force,
                CancellationToken.None));
    }

    private async void Cancel_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (!_waitingForLogin)
        {
            CloseWindow(dialogResult: false);
            return;
        }

        await CancelLoginAsync();
    }

    private async Task RunAsync(
        Func<bool, Task<CreateProfileLoginResult>> action)
    {
        if (_operationInProgress)
        {
            return;
        }

        SetBusy(true);
        try
        {
            var result = await action(false);
            if (result.Status ==
                CreateProfileLoginStatus.ForceCloseConfirmationRequired)
            {
                if (!ConfirmForceClose())
                {
                    StatusTextBlock.Text =
                        "강제 종료하지 않았습니다. 현재 Codex 로그인 상태를 유지합니다.";
                    return;
                }

                result = await action(true);
            }

            HandleResult(result);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void HandleResult(CreateProfileLoginResult result)
    {
        switch (result.Status)
        {
            case CreateProfileLoginStatus.WaitingForLogin:
                _waitingForLogin = true;
                ProfileNameTextBox.IsEnabled = false;
                StartButton.Visibility = Visibility.Collapsed;
                CompleteButton.Visibility = Visibility.Visible;
                StatusTextBlock.Text =
                    "Codex에서 로그인을 마친 뒤 이 창으로 돌아와 ‘로그인 완료’를 누르세요.";
                break;

            case CreateProfileLoginStatus.Created:
                _waitingForLogin = false;
                CloseWindow(dialogResult: true);
                break;

            case CreateProfileLoginStatus.Canceled:
                _waitingForLogin = false;
                CloseWindow(dialogResult: false);
                break;

            case CreateProfileLoginStatus.InvalidName:
                StatusTextBlock.Text = "프로필 이름을 입력하세요.";
                break;

            case CreateProfileLoginStatus.DuplicateName:
                StatusTextBlock.Text =
                    "같은 이름의 프로필이 이미 있습니다.";
                break;

            case CreateProfileLoginStatus.StorageNeedsAttention:
                StatusTextBlock.Text =
                    "프로필 저장소에 읽을 수 없는 항목이 있어 새 프로필을 만들 수 없습니다.";
                break;

            case CreateProfileLoginStatus.LoginNotCompleted:
                ResetToStart(
                    "로그인 정보를 찾지 못했습니다. Codex 로그인을 완료한 뒤 다시 시작하세요.");
                break;

            case CreateProfileLoginStatus.InstallationNotFound:
                ResetToStart(
                    "실행할 수 있는 Microsoft Store형 Codex를 찾지 못했습니다.");
                break;

            case CreateProfileLoginStatus.RecoveryRequired:
                StatusTextBlock.Text =
                    "기존 인증 상태 복구가 필요합니다. 이 창을 닫고 메인 화면에서 복구하세요.";
                break;

            default:
                StatusTextBlock.Text =
                    "작업을 완료하지 못했습니다. Codex가 종료됐는지 확인한 뒤 다시 시도하세요.";
                break;
        }
    }

    private Task CancelLoginAsync()
    {
        return RunAsync(
            force => _profileLogin.CancelAsync(
                force,
                CancellationToken.None));
    }

    private void ResetToStart(string message)
    {
        _waitingForLogin = false;
        ProfileNameTextBox.IsEnabled = true;
        StartButton.Visibility = Visibility.Visible;
        CompleteButton.Visibility = Visibility.Collapsed;
        StatusTextBlock.Text = message;
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

    private void SetBusy(bool busy)
    {
        _operationInProgress = busy;
        CancelButton.IsEnabled = !busy;
        StartButton.IsEnabled = !busy;
        CompleteButton.IsEnabled = !busy;
        if (busy)
        {
            StatusTextBlock.Text = "처리 중...";
        }
    }

    private void CloseWindow(bool dialogResult)
    {
        _allowClose = true;
        DialogResult = dialogResult;
        Close();
    }

    private async void OnClosing(
        object? sender,
        CancelEventArgs e)
    {
        if (_allowClose || !_waitingForLogin)
        {
            return;
        }

        e.Cancel = true;
        if (!_operationInProgress)
        {
            await CancelLoginAsync();
        }
    }
}
