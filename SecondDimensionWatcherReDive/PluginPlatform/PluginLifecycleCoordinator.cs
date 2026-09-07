using System.Collections.Concurrent;

namespace SecondDimensionWatcherReDive.PluginPlatform;

internal sealed class PluginInvocationInterruptedException(string message) : InvalidOperationException(message);

internal sealed class PluginLifecycleCoordinator
{
    private readonly ConcurrentDictionary<string, Gate> _gates = new(StringComparer.Ordinal);

    public InvocationLease EnterInvocation(string pluginId, CancellationToken callerCancellationToken)
        => _gates.GetOrAdd(pluginId, _ => new Gate()).Enter(callerCancellationToken);

    public Task<IDisposable> BeginLifecycleAsync(
        string pluginId,
        TimeSpan timeout,
        CancellationToken cancellationToken)
        => _gates.GetOrAdd(pluginId, _ => new Gate()).BeginLifecycleAsync(timeout, cancellationToken);

    internal sealed class InvocationLease : IDisposable
    {
        private readonly Gate _owner;
        private readonly CancellationTokenSource _linkedCancellation;
        private int _disposed;

        public InvocationLease(
            Gate owner,
            CancellationToken lifecycleCancellationToken,
            CancellationToken callerCancellationToken)
        {
            _owner = owner;
            LifecycleCancellationToken = lifecycleCancellationToken;
            _linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                lifecycleCancellationToken, callerCancellationToken);
        }

        public CancellationToken Token => _linkedCancellation.Token;
        public CancellationToken LifecycleCancellationToken { get; }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            _linkedCancellation.Dispose();
            _owner.ExitInvocation();
        }
    }

    internal sealed class Gate
    {
        private readonly object _sync = new();
        private CancellationTokenSource _lifecycleCancellation = new();
        private TaskCompletionSource? _drained;
        private int _active;
        private bool _lifecycleActive;

        public InvocationLease Enter(CancellationToken callerCancellationToken)
        {
            lock (_sync)
            {
                if (_lifecycleActive)
                    throw new PluginCapacityExceededException(
                        "Plugin is being disabled, upgraded, or uninstalled.");
                _active++;
                return new InvocationLease(this, _lifecycleCancellation.Token, callerCancellationToken);
            }
        }

        public async Task<IDisposable> BeginLifecycleAsync(
            TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            Task drained;
            lock (_sync)
            {
                if (_lifecycleActive)
                    throw new InvalidOperationException("A lifecycle operation is already in progress for this plugin.");
                _lifecycleActive = true;
                _lifecycleCancellation.Cancel();
                if (_active == 0)
                {
                    drained = Task.CompletedTask;
                }
                else
                {
                    _drained = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    drained = _drained.Task;
                }
            }

            try
            {
                await drained.WaitAsync(timeout, cancellationToken);
                return new LifecycleLease(this);
            }
            catch
            {
                EndLifecycle();
                throw;
            }
        }

        public void ExitInvocation()
        {
            lock (_sync)
            {
                _active--;
                if (_active == 0) _drained?.TrySetResult();
            }
        }

        private void EndLifecycle()
        {
            lock (_sync)
            {
                if (!_lifecycleActive) return;
                _lifecycleCancellation.Dispose();
                _lifecycleCancellation = new CancellationTokenSource();
                _drained = null;
                _lifecycleActive = false;
            }
        }

        private sealed class LifecycleLease(Gate owner) : IDisposable
        {
            private int _disposed;

            public void Dispose()
            {
                if (Interlocked.Exchange(ref _disposed, 1) == 0) owner.EndLifecycle();
            }
        }
    }
}
