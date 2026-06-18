using System.ComponentModel;
using System.Runtime.CompilerServices;
using CodexSwitcher.Core.Installation;

namespace CodexSwitcher.Bootstrapper.Presentation;

public sealed class MainWindowViewModel : INotifyPropertyChanged
{
    private readonly DetectCodexInstallationUseCase _detectInstallation;
    private string _installationStatusMessage = "Codex 설치 확인 중...";

    public MainWindowViewModel(
        DetectCodexInstallationUseCase detectInstallation)
    {
        _detectInstallation = detectInstallation;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string InstallationStatusMessage
    {
        get => _installationStatusMessage;
        private set
        {
            if (_installationStatusMessage == value)
            {
                return;
            }

            _installationStatusMessage = value;
            OnPropertyChanged();
        }
    }

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        var result = await _detectInstallation.ExecuteAsync(cancellationToken);
        InstallationStatusMessage = result.Status switch
        {
            CodexInstallationDetectionStatus.Installed => "Codex 설치 확인됨",
            CodexInstallationDetectionStatus.NotInstalled => "Codex를 찾지 못했습니다",
            _ => "Codex 설치 확인 중 오류가 발생했습니다"
        };
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(
            this,
            new PropertyChangedEventArgs(propertyName));
    }
}

