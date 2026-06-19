namespace CodexSwitcher.Bootstrapper;

internal sealed class SingleInstanceCoordinator : IDisposable
{
    private const string InstanceMutexName =
        @"Local\CodexSwitcher.SingleInstance";
    private const string ActivationEventName =
        @"Local\CodexSwitcher.Activate";

    private readonly Mutex _instanceMutex;
    private readonly EventWaitHandle _activationEvent;
    private CancellationTokenSource? _listenerCancellation;
    private Task? _listenerTask;

    public SingleInstanceCoordinator()
    {
        _activationEvent = new EventWaitHandle(
            initialState: false,
            EventResetMode.AutoReset,
            ActivationEventName);
        _instanceMutex = new Mutex(
            initiallyOwned: false,
            InstanceMutexName,
            out var createdNew);
        IsPrimaryInstance = createdNew;
    }

    public bool IsPrimaryInstance { get; }

    public void SignalPrimaryInstance()
    {
        _activationEvent.Set();
    }

    public void StartListening(Action activationRequested)
    {
        if (!IsPrimaryInstance ||
            _listenerCancellation is not null)
        {
            return;
        }

        _listenerCancellation = new CancellationTokenSource();
        var cancellationToken = _listenerCancellation.Token;
        _listenerTask = Task.Run(
            () => Listen(
                activationRequested,
                cancellationToken),
            cancellationToken);
    }

    public void Dispose()
    {
        _listenerCancellation?.Cancel();

        if (_listenerTask is not null)
        {
            try
            {
                _listenerTask.Wait(TimeSpan.FromSeconds(1));
            }
            catch (AggregateException exception)
                when (exception.InnerExceptions.All(
                    inner => inner is OperationCanceledException))
            {
                // 종료 신호로 끝난 대기 작업이다.
            }
        }

        _listenerCancellation?.Dispose();
        _activationEvent.Dispose();
        _instanceMutex.Dispose();
    }

    private void Listen(
        Action activationRequested,
        CancellationToken cancellationToken)
    {
        var waitHandles = new WaitHandle[]
        {
            _activationEvent,
            cancellationToken.WaitHandle
        };

        while (!cancellationToken.IsCancellationRequested)
        {
            var signaled = WaitHandle.WaitAny(waitHandles);
            if (signaled != 0)
            {
                return;
            }

            activationRequested();
        }
    }
}
