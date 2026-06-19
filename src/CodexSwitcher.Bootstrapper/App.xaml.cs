using System.ComponentModel;
using System.Windows;
using CodexSwitcher.Bootstrapper.Presentation;
using CodexSwitcher.Bootstrapper.Usage;
using CodexSwitcher.Core.Installation;
using CodexSwitcher.Core.Profiles;
using CodexSwitcher.Core.Usage;
using CodexSwitcher.Infrastructure.Installation;
using CodexSwitcher.Infrastructure.Profiles;
using CodexSwitcher.Infrastructure.Usage;

namespace CodexSwitcher.Bootstrapper;

public partial class App : Application
{
    private SingleInstanceCoordinator? _singleInstance;
    private TrayIconService? _trayIcon;
    private MainWindowViewModel? _viewModel;
    private ProfileUsageMonitor? _usageMonitor;
    private bool _isExiting;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _singleInstance = new SingleInstanceCoordinator();
        if (!_singleInstance.IsPrimaryInstance)
        {
            _singleInstance.SignalPrimaryInstance();
            Shutdown();
            return;
        }

        _singleInstance.StartListening(
            () => Dispatcher.BeginInvoke(
                ActivateApplicationWindow));

        var installationLocator = new WindowsCodexInstallationLocator();
        var detectInstallation = new DetectCodexInstallationUseCase(
            installationLocator);
        var profileStore = new WindowsProfileStore();
        var listProfiles = new ListProfilesUseCase(profileStore);
        var authenticationSession =
            new WindowsLoginAuthenticationSession();
        var codexController = new WindowsCodexLoginController(
            installationLocator);
        var operationCoordinator =
            new ProfileOperationCoordinator();
        var profileLogin = new CreateProfileLoginUseCase(
            profileStore,
            authenticationSession,
            codexController,
            operationCoordinator);
        var runProfile = new RunProfileUseCase(
            profileStore,
            authenticationSession,
            codexController,
            operationCoordinator);
        var switchProfile = new SwitchProfileUseCase(
            profileStore,
            authenticationSession,
            codexController,
            operationCoordinator);
        var deleteProfile = new DeleteProfileUseCase(
            profileStore,
            authenticationSession,
            codexController,
            operationCoordinator);
        var getRuntimeState =
            new GetProfileRuntimeStateUseCase(
                profileStore,
                authenticationSession,
                codexController);
        var rateLimitReader =
            new WindowsCodexRateLimitReader();
        var refreshProfileRateLimit =
            new RefreshProfileRateLimitUseCase(
                profileStore,
                rateLimitReader,
                operationCoordinator);
        var usageMonitor = new ProfileUsageMonitor(
            listProfiles,
            getRuntimeState,
            refreshProfileRateLimit,
            rateLimitReader);
        var viewModel = new MainWindowViewModel(
            detectInstallation,
            listProfiles,
            profileLogin,
            runProfile,
            switchProfile,
            deleteProfile,
            getRuntimeState);
        var window = new MainWindow(
            viewModel,
            profileLogin,
            usageMonitor);

        _viewModel = viewModel;
        _usageMonitor = usageMonitor;
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;

        MainWindow = window;
        window.Show();

        _trayIcon = new TrayIconService(
            () => Dispatcher.BeginInvoke(
                ActivateApplicationWindow),
            () => Dispatcher.BeginInvoke(
                RequestApplicationExit));
        _trayIcon.UpdateStatus(viewModel.RuntimeStatusMessage);

        await viewModel.InitializeAsync(CancellationToken.None);
        window.StartRuntimeMonitoring();
        window.StartUsageMonitoring();
    }

    protected override void OnSessionEnding(
        SessionEndingCancelEventArgs e)
    {
        PrepareForExit();
        base.OnSessionEnding(e);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged -=
                OnViewModelPropertyChanged;
        }

        _trayIcon?.Dispose();
        _trayIcon = null;
        _usageMonitor?.Dispose();
        _usageMonitor = null;
        _singleInstance?.Dispose();
        _singleInstance = null;

        base.OnExit(e);
    }

    private void OnViewModelPropertyChanged(
        object? sender,
        PropertyChangedEventArgs e)
    {
        if (e.PropertyName ==
            nameof(MainWindowViewModel.RuntimeStatusMessage))
        {
            _trayIcon?.UpdateStatus(
                _viewModel?.RuntimeStatusMessage ?? "");
        }
    }

    private void ActivateApplicationWindow()
    {
        if (_isExiting ||
            MainWindow is not MainWindow mainWindow)
        {
            return;
        }

        if (!mainWindow.IsVisible)
        {
            mainWindow.Show();
        }

        RestoreAndActivate(mainWindow);

        var dialog = Windows
            .OfType<Window>()
            .LastOrDefault(
                window =>
                    window != mainWindow &&
                    window.IsVisible);
        if (dialog is not null)
        {
            RestoreAndActivate(dialog);
        }
    }

    private void RequestApplicationExit()
    {
        if (_isExiting ||
            MainWindow is not MainWindow mainWindow)
        {
            return;
        }

        var openDialog = Windows
            .OfType<Window>()
            .LastOrDefault(
                window =>
                    window != mainWindow &&
                    window.IsVisible);
        if (openDialog is not null)
        {
            MessageBox.Show(
                openDialog,
                "열려 있는 작업을 완료하거나 취소한 뒤 앱을 종료하세요.",
                "작업 진행 중",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            RestoreAndActivate(openDialog);
            return;
        }

        if (mainWindow.IsOperationInProgress)
        {
            MessageBox.Show(
                mainWindow,
                "프로필 작업이 진행 중입니다. 작업이 끝난 뒤 앱을 종료하세요.",
                "작업 진행 중",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            ActivateApplicationWindow();
            return;
        }

        PrepareForExit();
        Shutdown();
    }

    private void PrepareForExit()
    {
        if (_isExiting)
        {
            return;
        }

        _isExiting = true;
        if (MainWindow is MainWindow mainWindow)
        {
            mainWindow.AllowApplicationExit();
        }
    }

    private static void RestoreAndActivate(Window window)
    {
        if (window.WindowState == WindowState.Minimized)
        {
            window.WindowState = WindowState.Normal;
        }

        _ = window.Activate();
        window.Topmost = true;
        window.Topmost = false;
        _ = window.Focus();
    }
}
