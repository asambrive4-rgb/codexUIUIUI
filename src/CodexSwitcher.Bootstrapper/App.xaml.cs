using System.Windows;
using CodexSwitcher.Bootstrapper.Presentation;
using CodexSwitcher.Core.Installation;
using CodexSwitcher.Infrastructure.Installation;

namespace CodexSwitcher.Bootstrapper;

public partial class App : Application
{
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var installationLocator = new WindowsCodexInstallationLocator();
        var detectInstallation = new DetectCodexInstallationUseCase(
            installationLocator);
        var viewModel = new MainWindowViewModel(detectInstallation);
        var window = new MainWindow(viewModel);

        MainWindow = window;
        window.Show();

        await viewModel.InitializeAsync(CancellationToken.None);
    }
}

