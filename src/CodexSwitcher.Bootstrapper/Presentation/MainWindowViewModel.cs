using System.Collections.ObjectModel;
using CodexSwitcher.Core.Installation;
using CodexSwitcher.Core.Profiles;
using CodexSwitcher.Core.Usage;

namespace CodexSwitcher.Bootstrapper.Presentation;

public sealed class MainWindowViewModel : ObservableObject
{
    private readonly DetectCodexInstallationUseCase _detectInstallation;
    private readonly ListProfilesUseCase _listProfiles;
    private readonly CreateProfileLoginUseCase _profileLogin;
    private readonly RunProfileUseCase _runProfile;
    private readonly SwitchProfileUseCase _switchProfile;
    private readonly DeleteProfileUseCase _deleteProfile;
    private readonly GetProfileRuntimeStateUseCase _getRuntimeState;
    private readonly ProfileListPresentationState _profileList = new();
    private readonly SemaphoreSlim _runtimeRefreshGate = new(1, 1);
    private ProfileRuntimeState? _lastRuntimeState;
    private bool? _lastRuntimeCanOperate;
    private string _installationStatusMessage = "확인 중";
    private string _profileStatusMessage = "프로필을 불러오는 중...";
    private string _runtimeStatusMessage = "Codex 상태 확인 중...";
    private string _operationStatusMessage = "";
    private bool _hasPendingRecovery;
    private bool _isOperationInProgress;
    private bool _isUsageRefreshing;
    private bool _isRuntimePresentationDirty = true;

    public MainWindowViewModel(
        DetectCodexInstallationUseCase detectInstallation,
        ListProfilesUseCase listProfiles,
        CreateProfileLoginUseCase profileLogin,
        RunProfileUseCase runProfile,
        SwitchProfileUseCase switchProfile,
        DeleteProfileUseCase deleteProfile,
        GetProfileRuntimeStateUseCase getRuntimeState)
    {
        _detectInstallation = detectInstallation;
        _listProfiles = listProfiles;
        _profileLogin = profileLogin;
        _runProfile = runProfile;
        _switchProfile = switchProfile;
        _deleteProfile = deleteProfile;
        _getRuntimeState = getRuntimeState;
    }

    public ObservableCollection<ProfileListItemViewModel> Profiles =>
        _profileList.Profiles;

    public ProfileListItemViewModel? ActiveProfile =>
        Profiles.FirstOrDefault(p => p.IsActive);

    public ProfileListItemViewModel? DefaultPopupProfile =>
        ActiveProfile ?? Profiles.FirstOrDefault();

    public bool HasActiveProfile => ActiveProfile is not null;

    public IEnumerable<ProfileListItemViewModel> InactiveProfiles =>
        Profiles.Where(p => !p.IsActive);

    public string InstallationStatusMessage
    {
        get => _installationStatusMessage;
        private set => SetField(ref _installationStatusMessage, value);
    }

    public string ProfileStatusMessage
    {
        get => _profileStatusMessage;
        private set => SetField(ref _profileStatusMessage, value);
    }

    public string RuntimeStatusMessage
    {
        get => _runtimeStatusMessage;
        private set => SetField(ref _runtimeStatusMessage, value);
    }

    public string OperationStatusMessage
    {
        get => _operationStatusMessage;
        private set => SetField(ref _operationStatusMessage, value);
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

    public bool IsOperationInProgress => _isOperationInProgress;

    public bool IsUsageRefreshing
    {
        get => _isUsageRefreshing;
        private set
        {
            if (SetField(ref _isUsageRefreshing, value))
            {
                OnPropertyChanged(nameof(CanRefreshUsage));
            }
        }
    }

    public bool CanRefreshUsage => !IsUsageRefreshing;

    public void SetUsageRefreshing(bool value) =>
        IsUsageRefreshing = value;

    public void ApplyUsageSnapshot(
        ProfileRateLimitSnapshot snapshot) =>
        _profileList.ApplyUsageSnapshot(snapshot);

    public async Task InitializeAsync(
        CancellationToken cancellationToken)
    {
        var result = await RunOffUiThreadAsync(
            token => _detectInstallation.ExecuteAsync(token),
            cancellationToken);
        InstallationStatusMessage = result.Status switch
        {
            CodexInstallationDetectionStatus.Installed =>
                "설치됨",
            CodexInstallationDetectionStatus.NotInstalled =>
                "미설치",
            _ => "오류"
        };

        await RefreshProfilesAsync(cancellationToken);
    }

    public async Task RefreshProfilesAsync(
        CancellationToken cancellationToken)
    {
        var result = await RunOffUiThreadAsync(
            token => _listProfiles.ExecuteAsync(token),
            cancellationToken);
        _profileList.Replace(result.Profiles);
        InvalidateRuntimePresentation();
        ProfileStatusMessage = result.Issues.Count > 0
            ? "일부 프로필 저장 데이터를 읽지 못했습니다. 새 프로필 추가 전에 저장소 확인이 필요합니다."
            : result.Profiles.Count == 0
                ? "저장된 프로필이 없습니다."
                : $"프로필 {result.Profiles.Count}개";

        await RefreshRuntimeStateAsync(
            cancellationToken,
            forceApply: true,
            forceRuntimeRefresh: true);
    }

    public Task RefreshRuntimeStateAsync(
        CancellationToken cancellationToken) =>
        RefreshRuntimeStateAsync(
            cancellationToken,
            forceApply: false,
            forceRuntimeRefresh: false);

    private async Task RefreshRuntimeStateAsync(
        CancellationToken cancellationToken,
        bool forceApply,
        bool forceRuntimeRefresh)
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
            var result = await RunOffUiThreadAsync(
                async token => new RuntimeRefreshResult(
                    await _profileLogin.HasPendingRecoveryAsync(token),
                    await _getRuntimeState.ExecuteAsync(
                        forceRuntimeRefresh,
                        token)),
                cancellationToken);

            HasPendingRecovery = result.HasPendingRecovery;
            ApplyRuntimePresentation(
                result.State,
                forceApply);
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
            _profileList.DisableAllActions();
            InvalidateRuntimePresentation();
        }
        finally
        {
            _runtimeRefreshGate.Release();
        }
    }

    public Task<RunProfileResult> RunProfileAsync(
        ProfileId profileId,
        CancellationToken cancellationToken) =>
        ExecuteProfileOperationAsync(
            profileId,
            "실행 중...",
            "선택한 프로필로 Codex 실행 중...",
            new RunProfileResult(RunProfileStatus.Failed),
            _runProfile.ExecuteAsync,
            result =>
                ProfileOperationMessageFormatter.Describe(result.Status),
            (_, token) => RefreshRuntimeStateAfterChangeAsync(token),
            cancellationToken);

    public Task<SwitchProfileResult> SwitchProfileAsync(
        ProfileId profileId,
        CancellationToken cancellationToken) =>
        ExecuteProfileOperationAsync(
            profileId,
            "전환 중...",
            "선택한 프로필로 Codex 전환 중...",
            new SwitchProfileResult(SwitchProfileStatus.Failed),
            _switchProfile.ExecuteAsync,
            result =>
                ProfileOperationMessageFormatter.Describe(result.Status),
            (_, token) => RefreshRuntimeStateAfterChangeAsync(token),
            cancellationToken);

    public Task<DeleteProfileResult> DeleteProfileAsync(
        ProfileId profileId,
        CancellationToken cancellationToken) =>
        ExecuteProfileOperationAsync(
            profileId,
            "삭제 중...",
            "선택한 프로필 삭제 중...",
            new DeleteProfileResult(DeleteProfileStatus.Failed),
            _deleteProfile.ExecuteAsync,
            result =>
                ProfileOperationMessageFormatter.Describe(result.Status),
            (result, token) =>
                result.Status == DeleteProfileStatus.Deleted
                    ? RefreshProfilesAsync(token)
                    : RefreshRuntimeStateAfterChangeAsync(token),
            cancellationToken);

    private bool CanOperateOnProfiles =>
        !HasPendingRecovery &&
        !_isOperationInProgress;

    private Task RefreshRuntimeStateAfterChangeAsync(
        CancellationToken cancellationToken) =>
        RefreshRuntimeStateAsync(
            cancellationToken,
            forceApply: true,
            forceRuntimeRefresh: true);

    private async Task<TResult> ExecuteProfileOperationAsync<TResult>(
        ProfileId profileId,
        string itemStatus,
        string operationStatus,
        TResult busyResult,
        Func<ProfileId, CancellationToken, Task<TResult>> execute,
        Func<TResult, string> describeResult,
        Func<TResult, CancellationToken, Task> refreshAfter,
        CancellationToken cancellationToken)
    {
        if (_isOperationInProgress)
        {
            return busyResult;
        }

        SetOperationInProgress(true);
        _profileList.BeginOperation(profileId, itemStatus);
        InvalidateRuntimePresentation();
        NotifyProfileCollectionsChanged();
        OperationStatusMessage = operationStatus;

        TResult result;
        try
        {
            result = await RunOffUiThreadAsync(
                token => execute(profileId, token),
                cancellationToken);
            OperationStatusMessage = describeResult(result);
        }
        finally
        {
            SetOperationInProgress(false);
        }

        HasPendingRecovery =
            await RunOffUiThreadAsync(
                token => _profileLogin.HasPendingRecoveryAsync(token),
                cancellationToken);
        await refreshAfter(result, cancellationToken);
        NotifyProfileCollectionsChanged();
        return result;
    }

    private void ApplyRuntimePresentation(
        ProfileRuntimeState state,
        bool forceApply)
    {
        var canOperate = CanOperateOnProfiles;
        if (!forceApply &&
            !_isRuntimePresentationDirty &&
            _lastRuntimeCanOperate == canOperate &&
            EqualityComparer<ProfileRuntimeState>.Default.Equals(
                _lastRuntimeState,
                state))
        {
            return;
        }

        var previousActiveProfileId = ActiveProfile?.Id;
        RuntimeStatusMessage = _profileList.ApplyRuntimeState(
            state,
            canOperate);
        _lastRuntimeState = state;
        _lastRuntimeCanOperate = canOperate;
        _isRuntimePresentationDirty = false;

        if (forceApply ||
            previousActiveProfileId != ActiveProfile?.Id)
        {
            NotifyProfileCollectionsChanged();
        }
    }

    private void InvalidateRuntimePresentation()
    {
        _isRuntimePresentationDirty = true;
    }

    private static async Task<TResult> RunOffUiThreadAsync<TResult>(
        Func<CancellationToken, Task<TResult>> action,
        CancellationToken cancellationToken)
    {
        return await Task.Run(
                () => action(cancellationToken),
                cancellationToken)
            .ConfigureAwait(false);
    }

    private void NotifyProfileCollectionsChanged()
    {
        OnPropertyChanged(nameof(ActiveProfile));
        OnPropertyChanged(nameof(HasActiveProfile));
        OnPropertyChanged(nameof(InactiveProfiles));
    }

    private void SetOperationInProgress(bool value)
    {
        if (_isOperationInProgress == value)
        {
            return;
        }

        _isOperationInProgress = value;
        OnPropertyChanged(nameof(CanAddProfile));
        OnPropertyChanged(nameof(IsOperationInProgress));
    }

    private sealed record RuntimeRefreshResult(
        bool HasPendingRecovery,
        ProfileRuntimeState State);
}
