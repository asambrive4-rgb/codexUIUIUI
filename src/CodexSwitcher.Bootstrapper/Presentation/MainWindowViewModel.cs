using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using CodexSwitcher.Core.Installation;
using CodexSwitcher.Core.Profiles;

namespace CodexSwitcher.Bootstrapper.Presentation;

public sealed class MainWindowViewModel : INotifyPropertyChanged
{
    private readonly DetectCodexInstallationUseCase _detectInstallation;
    private readonly ListProfilesUseCase _listProfiles;
    private readonly CreateProfileLoginUseCase _profileLogin;
    private readonly RunProfileUseCase _runProfile;
    private readonly SwitchProfileUseCase _switchProfile;
    private readonly GetProfileRuntimeStateUseCase _getRuntimeState;
    private readonly SemaphoreSlim _runtimeRefreshGate = new(1, 1);
    private string _installationStatusMessage = "Codex 설치 확인 중...";
    private string _profileStatusMessage = "프로필을 불러오는 중...";
    private string _runtimeStatusMessage = "Codex 상태 확인 중...";
    private string _operationStatusMessage = "";
    private bool _hasPendingRecovery;
    private bool _isOperationInProgress;

    public MainWindowViewModel(
        DetectCodexInstallationUseCase detectInstallation,
        ListProfilesUseCase listProfiles,
        CreateProfileLoginUseCase profileLogin,
        RunProfileUseCase runProfile,
        SwitchProfileUseCase switchProfile,
        GetProfileRuntimeStateUseCase getRuntimeState)
    {
        _detectInstallation = detectInstallation;
        _listProfiles = listProfiles;
        _profileLogin = profileLogin;
        _runProfile = runProfile;
        _switchProfile = switchProfile;
        _getRuntimeState = getRuntimeState;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<ProfileListItemViewModel> Profiles { get; } =
        [];

    public string InstallationStatusMessage
    {
        get => _installationStatusMessage;
        private set => SetField(
            ref _installationStatusMessage,
            value);
    }

    public string ProfileStatusMessage
    {
        get => _profileStatusMessage;
        private set => SetField(
            ref _profileStatusMessage,
            value);
    }

    public string RuntimeStatusMessage
    {
        get => _runtimeStatusMessage;
        private set => SetField(
            ref _runtimeStatusMessage,
            value);
    }

    public string OperationStatusMessage
    {
        get => _operationStatusMessage;
        private set => SetField(
            ref _operationStatusMessage,
            value);
    }

    public bool HasPendingRecovery
    {
        get => _hasPendingRecovery;
        private set
        {
            if (SetField(ref _hasPendingRecovery, value))
            {
                OnPropertyChanged(nameof(CanAddProfile));
            }
        }
    }

    public bool CanAddProfile =>
        !_isOperationInProgress &&
        !HasPendingRecovery;

    public async Task InitializeAsync(
        CancellationToken cancellationToken)
    {
        var result = await _detectInstallation.ExecuteAsync(
            cancellationToken);
        InstallationStatusMessage = result.Status switch
        {
            CodexInstallationDetectionStatus.Installed =>
                "Codex 설치 확인됨",
            CodexInstallationDetectionStatus.NotInstalled =>
                "Codex를 찾지 못했습니다",
            _ => "Codex 설치 확인 중 오류가 발생했습니다"
        };

        await RefreshProfilesAsync(cancellationToken);
    }

    public async Task RefreshProfilesAsync(
        CancellationToken cancellationToken)
    {
        var result = await _listProfiles.ExecuteAsync(
            cancellationToken);
        Profiles.Clear();

        foreach (var profile in result.Profiles)
        {
            Profiles.Add(
                new ProfileListItemViewModel(
                    profile.Id,
                    profile.Name.Value));
        }

        ProfileStatusMessage = result.Issues.Count > 0
            ? "일부 프로필 저장 데이터를 읽지 못했습니다. 새 프로필 추가 전에 저장소 확인이 필요합니다."
            : result.Profiles.Count == 0
                ? "저장된 프로필이 없습니다."
                : $"프로필 {result.Profiles.Count}개";

        HasPendingRecovery =
            await _profileLogin.HasPendingRecoveryAsync(
                cancellationToken);
        await RefreshRuntimeStateAsync(cancellationToken);
    }

    public async Task RefreshRuntimeStateAsync(
        CancellationToken cancellationToken)
    {
        if (_isOperationInProgress ||
            !await _runtimeRefreshGate.WaitAsync(
                millisecondsTimeout: 0,
                cancellationToken))
        {
            return;
        }

        try
        {
            HasPendingRecovery =
                await _profileLogin.HasPendingRecoveryAsync(
                    cancellationToken);
            var state = await _getRuntimeState.ExecuteAsync(
                cancellationToken);
            ApplyRuntimeState(state);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            RuntimeStatusMessage =
                "Codex 상태를 확인하지 못했습니다.";
            DisableAllRunButtons();
        }
        finally
        {
            _runtimeRefreshGate.Release();
        }
    }

    public async Task<RunProfileResult> RunProfileAsync(
        ProfileId profileId,
        CancellationToken cancellationToken)
    {
        if (_isOperationInProgress)
        {
            return new RunProfileResult(
                RunProfileStatus.Failed);
        }

        SetOperationInProgress(true);
        var target = Profiles.FirstOrDefault(
            profile => profile.Id == profileId);
        if (target is not null)
        {
            target.Status = "실행 중...";
        }

        OperationStatusMessage = "선택한 프로필로 Codex 실행 중...";
        DisableAllRunButtons();

        RunProfileResult result;
        try
        {
            result = await _runProfile.ExecuteAsync(
                profileId,
                cancellationToken);
            OperationStatusMessage = DescribeRunResult(result.Status);
        }
        finally
        {
            SetOperationInProgress(false);
        }

        HasPendingRecovery =
            await _profileLogin.HasPendingRecoveryAsync(
                cancellationToken);
        await RefreshRuntimeStateAsync(cancellationToken);
        return result;
    }

    public async Task<SwitchProfileResult> SwitchProfileAsync(
        ProfileId profileId,
        bool forceCloseApproved,
        CancellationToken cancellationToken)
    {
        if (_isOperationInProgress)
        {
            return new SwitchProfileResult(
                SwitchProfileStatus.Failed);
        }

        SetOperationInProgress(true);
        var target = Profiles.FirstOrDefault(
            profile => profile.Id == profileId);
        if (target is not null)
        {
            target.Status = "전환 중...";
        }

        OperationStatusMessage =
            "선택한 프로필로 Codex 전환 중...";
        DisableAllRunButtons();

        SwitchProfileResult result;
        try
        {
            result = await _switchProfile.ExecuteAsync(
                profileId,
                forceCloseApproved,
                cancellationToken);
            OperationStatusMessage =
                DescribeSwitchResult(result.Status);
        }
        finally
        {
            SetOperationInProgress(false);
        }

        HasPendingRecovery =
            await _profileLogin.HasPendingRecoveryAsync(
                cancellationToken);
        await RefreshRuntimeStateAsync(cancellationToken);
        return result;
    }

    private void ApplyRuntimeState(ProfileRuntimeState state)
    {
        switch (state.Status)
        {
            case ProfileRuntimeStatus.Stopped:
                RuntimeStatusMessage = "Codex 종료됨";
                foreach (var profile in Profiles)
                {
                    profile.IsActive = false;
                    profile.Status = "준비됨";
                    profile.ButtonText = "실행";
                    profile.IsSwitchAction = false;
                    profile.IsRunEnabled =
                        !HasPendingRecovery &&
                        !_isOperationInProgress;
                }

                break;

            case ProfileRuntimeStatus.RunningKnownProfile:
                var active = Profiles.FirstOrDefault(
                    profile =>
                        profile.Id == state.ActiveProfileId);
                RuntimeStatusMessage = active is null
                    ? "Codex 실행 중 · 프로필 확인 불가"
                    : $"Codex 실행 중 · {active.Name}";

                foreach (var profile in Profiles)
                {
                    profile.IsActive = active is not null &&
                                       profile.Id == active.Id;
                    profile.Status = profile.IsActive
                        ? "실행 중"
                        : "준비됨";
                    profile.ButtonText = profile.IsActive
                        ? "실행 중"
                        : "전환";
                    profile.IsSwitchAction =
                        active is not null &&
                        !profile.IsActive;
                    profile.IsRunEnabled =
                        !profile.IsActive &&
                        active is not null &&
                        !HasPendingRecovery &&
                        !_isOperationInProgress;
                }

                break;

            default:
                RuntimeStatusMessage =
                    "Codex 실행 중 · 프로필 확인 불가";
                foreach (var profile in Profiles)
                {
                    profile.IsActive = false;
                    profile.Status = "준비됨";
                    profile.ButtonText = "전환";
                    profile.IsSwitchAction = true;
                    profile.IsRunEnabled = false;
                }

                break;
        }
    }

    private void DisableAllRunButtons()
    {
        foreach (var profile in Profiles)
        {
            profile.IsRunEnabled = false;
        }
    }

    private void SetOperationInProgress(bool value)
    {
        if (_isOperationInProgress == value)
        {
            return;
        }

        _isOperationInProgress = value;
        OnPropertyChanged(nameof(CanAddProfile));
    }

    private static string DescribeRunResult(RunProfileStatus status)
    {
        return status switch
        {
            RunProfileStatus.Running =>
                "Codex를 실행했습니다.",
            RunProfileStatus.AlreadyRunning =>
                "이미 Codex가 실행 중입니다.",
            RunProfileStatus.ProfileNotFound =>
                "선택한 프로필을 찾을 수 없습니다.",
            RunProfileStatus.InstallationNotFound =>
                "실행할 수 있는 Codex 설치를 찾지 못했습니다.",
            RunProfileStatus.LaunchFailed =>
                "Codex 실행에 실패했습니다. 이전 인증 상태를 복구했습니다.",
            RunProfileStatus.AuthenticationMismatch =>
                "인증 상태 확인에 실패해 이전 상태를 복구했습니다.",
            RunProfileStatus.RecoveryRequired =>
                "인증 복구가 필요합니다. 이전 로그인 작업 복구를 실행하세요.",
            _ => "프로필 실행 중 오류가 발생했습니다."
        };
    }

    private static string DescribeSwitchResult(SwitchProfileStatus status)
    {
        return status switch
        {
            SwitchProfileStatus.Switched =>
                "프로필을 전환했습니다.",
            SwitchProfileStatus.AlreadyRunningTarget =>
                "이미 선택한 프로필로 실행 중입니다.",
            SwitchProfileStatus.ProfileNotFound =>
                "선택한 프로필을 찾을 수 없습니다.",
            SwitchProfileStatus.RunningUnknownProfile =>
                "현재 실행 중인 Codex 프로필을 확인할 수 없어 전환하지 않았습니다.",
            SwitchProfileStatus.ForceCloseConfirmationRequired =>
                "Codex가 정상 종료되지 않았습니다. 강제 종료 확인이 필요합니다.",
            SwitchProfileStatus.InstallationNotFound =>
                "실행할 수 있는 Codex 설치를 찾지 못했습니다.",
            SwitchProfileStatus.LaunchFailed =>
                "Codex 재실행에 실패했습니다. 이전 인증 상태를 복구했습니다.",
            SwitchProfileStatus.AuthenticationMismatch =>
                "전환 후 인증 상태 확인에 실패해 이전 상태를 복구했습니다.",
            SwitchProfileStatus.RecoveryRequired =>
                "인증 복구가 필요합니다. 이전 로그인 작업 복구를 실행하세요.",
            _ => "프로필 전환 중 오류가 발생했습니다."
        };
    }

    private bool SetField<T>(
        ref T field,
        T value,
        [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged(
        [CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(
            this,
            new PropertyChangedEventArgs(propertyName));
    }
}

public sealed class ProfileListItemViewModel : INotifyPropertyChanged
{
    private string _status = "준비됨";
    private string _buttonText = "실행";
    private bool _isRunEnabled = true;
    private bool _isSwitchAction;
    private bool _isActive;

    public ProfileListItemViewModel(
        ProfileId id,
        string name)
    {
        Id = id;
        Name = name;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ProfileId Id { get; }

    public string Name { get; }

    public string Status
    {
        get => _status;
        set => SetField(ref _status, value);
    }

    public bool IsRunEnabled
    {
        get => _isRunEnabled;
        set => SetField(ref _isRunEnabled, value);
    }

    public string ButtonText
    {
        get => _buttonText;
        set => SetField(ref _buttonText, value);
    }

    public bool IsSwitchAction
    {
        get => _isSwitchAction;
        set => SetField(ref _isSwitchAction, value);
    }

    public bool IsActive
    {
        get => _isActive;
        set => SetField(ref _isActive, value);
    }

    private void SetField<T>(
        ref T field,
        T value,
        [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return;
        }

        field = value;
        PropertyChanged?.Invoke(
            this,
            new PropertyChangedEventArgs(propertyName));
    }
}
