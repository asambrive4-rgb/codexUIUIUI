using System.Windows;
using CodexSwitcher.Bootstrapper.Presentation;
using CodexSwitcher.Core.Installation;
using CodexSwitcher.Core.Profiles;
using CodexSwitcher.Infrastructure.Installation;
using CodexSwitcher.Infrastructure.Profiles;

namespace CodexSwitcher.Bootstrapper;

public partial class App : Application
{
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

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
        var viewModel = new MainWindowViewModel(
            detectInstallation,
            listProfiles,
            profileLogin,
            runProfile,
            switchProfile,
            deleteProfile,
            getRuntimeState);
        var window = new MainWindow(viewModel, profileLogin);

        MainWindow = window;
        window.Show();

        await viewModel.InitializeAsync(CancellationToken.None);
        window.StartRuntimeMonitoring();
    }
}
