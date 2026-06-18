using CodexSwitcher.Core.Installation;

namespace CodexSwitcher.Tests.Installation;

[TestClass]
public sealed class DetectCodexInstallationUseCaseTests
{
    [TestMethod]
    public async Task ExecuteAsync_WhenInstalled_ReturnsInstalled()
    {
        var useCase = new DetectCodexInstallationUseCase(
            new StubInstallationLocator(_ => Task.FromResult(true)));

        var result = await useCase.ExecuteAsync(CancellationToken.None);

        Assert.AreEqual(
            CodexInstallationDetectionStatus.Installed,
            result.Status);
    }

    [TestMethod]
    public async Task ExecuteAsync_WhenNotInstalled_ReturnsNotInstalled()
    {
        var useCase = new DetectCodexInstallationUseCase(
            new StubInstallationLocator(_ => Task.FromResult(false)));

        var result = await useCase.ExecuteAsync(CancellationToken.None);

        Assert.AreEqual(
            CodexInstallationDetectionStatus.NotInstalled,
            result.Status);
    }

    [TestMethod]
    public async Task ExecuteAsync_WhenLocatorFails_ReturnsFailed()
    {
        var useCase = new DetectCodexInstallationUseCase(
            new StubInstallationLocator(
                _ => Task.FromException<bool>(
                    new InvalidOperationException("기술 오류"))));

        var result = await useCase.ExecuteAsync(CancellationToken.None);

        Assert.AreEqual(
            CodexInstallationDetectionStatus.Failed,
            result.Status);
    }

    [TestMethod]
    public async Task ExecuteAsync_WhenCanceled_PropagatesCancellation()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var useCase = new DetectCodexInstallationUseCase(
            new StubInstallationLocator(
                token => Task.FromCanceled<bool>(token)));

        await Assert.ThrowsExactlyAsync<TaskCanceledException>(
            () => useCase.ExecuteAsync(cancellation.Token));
    }

    private sealed class StubInstallationLocator : ICodexInstallationLocator
    {
        private readonly Func<CancellationToken, Task<bool>> _behavior;

        public StubInstallationLocator(
            Func<CancellationToken, Task<bool>> behavior)
        {
            _behavior = behavior;
        }

        public Task<bool> IsInstalledAsync(CancellationToken cancellationToken)
        {
            return _behavior(cancellationToken);
        }
    }
}

