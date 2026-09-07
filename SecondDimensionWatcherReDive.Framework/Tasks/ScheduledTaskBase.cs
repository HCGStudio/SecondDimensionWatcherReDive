using System.Threading.Channels;

namespace SecondDimensionWatcherReDive.Framework.Tasks;

public abstract class ScheduledTaskBase : IScheduledTask
{
    private readonly Channel<byte> _runQueue = Channel.CreateBounded<byte>(
        new BoundedChannelOptions(1)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.DropWrite
        });
    private readonly object _sync = new();
    private TaskCompletionSource<bool>? _pendingRun;
    private bool _pendingForce;
    private volatile bool _isRunning;
    private DateTimeOffset? _lastRunAt;

    public abstract string Id { get; }
    public abstract TimeSpan Interval { get; }
    public virtual bool IsEnabled => true;
    public DateTimeOffset? LastRunAt => _lastRunAt;
    public bool IsRunning => _isRunning;

    public async Task RunNowAsync(CancellationToken cancellationToken)
    {
        var completion = QueueRun(force: true);
        // Cancelling one HTTP request must not cancel the shared execution that
        // other callers and the periodic scheduler are awaiting.
        await completion.WaitAsync(cancellationToken);
    }

    /// <summary>
    ///     Runs a periodic signal and reports whether this instance acquired the
    ///     distributed lease. Hosting services use the result to poll quickly
    ///     while another instance owns an unfinished run.
    /// </summary>
    public Task<bool> RunScheduledAsync(CancellationToken cancellationToken) =>
        QueueRun(force: false).WaitAsync(cancellationToken);

    /// <summary>
    ///     Coalesces a run request without waiting for completion. At most one
    ///     pending signal exists while the current execution is in flight.
    /// </summary>
    public void Enqueue() => QueueRun(force: true);

    /// <summary>
    ///     Sequentially processes coalesced run requests. A PostgreSQL lease
    ///     ensures only one application instance executes a task at a time.
    /// </summary>
    public async Task ProcessQueueAsync(
        IScheduledTaskLeaseManager leaseManager,
        CancellationToken cancellationToken)
    {
        await foreach (var _ in _runQueue.Reader.ReadAllAsync(cancellationToken))
        {
            TaskCompletionSource<bool>? completion;
            lock (_sync)
            {
                completion = _pendingRun;
            }
            if (completion is null) continue;

            IScheduledTaskExecutionLease? lease = null;
            while (lease is null)
            {
                var force = TakePendingForce(completion);
                try
                {
                    lease = await leaseManager.TryAcquireAsync(
                        Id,
                        Interval,
                        force,
                        cancellationToken);
                }
                catch (OperationCanceledException exception) when (cancellationToken.IsCancellationRequested)
                {
                    completion.TrySetCanceled(exception.CancellationToken);
                    FinishRun(completion);
                    throw;
                }
                catch (Exception exception)
                {
                    completion.TrySetException(
                        new ScheduledTaskLeaseUnavailableException(exception));
                    FinishRun(completion);
                    break;
                }

                if (lease is not null)
                    break;

                // A manual request can upgrade a periodic acquisition while the
                // database call is in flight. Retry that upgrade before completing
                // the shared signal so a completed cooldown cannot swallow it.
                if (CompleteLeaseDenialOrRetryForce(completion, force))
                    continue;
                break;
            }
            if (lease is null) continue;

            await using (lease)
            {
                using var executionCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                    cancellationToken,
                    lease.LeaseLostToken);
                _isRunning = true;
                try
                {
                    await ExecuteTaskAsync(executionCancellation.Token);
                    _lastRunAt = DateTimeOffset.UtcNow;
                    await lease.CompleteAsync(true, null, cancellationToken);
                    completion.TrySetResult(true);
                }
                catch (OperationCanceledException exception)
                {
                    // On host shutdown or lease loss, leave the lease to expire so
                    // another instance can resume without an overlapping run.
                    completion.TrySetCanceled(exception.CancellationToken);
                    if (cancellationToken.IsCancellationRequested)
                        throw;
                }
                catch (Exception exception)
                {
                    try
                    {
                        await lease.CompleteAsync(
                            false,
                            exception.GetType().Name,
                            cancellationToken);
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception leaseException)
                    {
                        completion.TrySetException(
                            new ScheduledTaskLeaseUnavailableException(leaseException));
                        continue;
                    }
                    completion.TrySetException(exception);
                }
                finally
                {
                    _isRunning = false;
                    FinishRun(completion);
                }
            }
        }
    }

    protected abstract Task ExecuteTaskAsync(CancellationToken cancellationToken);

    private Task<bool> QueueRun(bool force)
    {
        lock (_sync)
        {
            if (_pendingRun is { Task.IsCompleted: false })
            {
                _pendingForce |= force;
                return _pendingRun.Task;
            }

            _pendingForce = force;
            _pendingRun = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            _runQueue.Writer.TryWrite(0);
            return _pendingRun.Task;
        }
    }

    private bool TakePendingForce(TaskCompletionSource<bool> completion)
    {
        lock (_sync)
        {
            if (!ReferenceEquals(_pendingRun, completion))
                return false;
            var force = _pendingForce;
            _pendingForce = false;
            return force;
        }
    }

    private bool CompleteLeaseDenialOrRetryForce(
        TaskCompletionSource<bool> completion,
        bool attemptedForce)
    {
        lock (_sync)
        {
            if (!ReferenceEquals(_pendingRun, completion))
                return false;
            if (!attemptedForce && _pendingForce)
                return true;

            _pendingRun = null;
            _pendingForce = false;
            completion.TrySetResult(false);
            return false;
        }
    }

    private void FinishRun(TaskCompletionSource<bool> completion)
    {
        lock (_sync)
        {
            if (ReferenceEquals(_pendingRun, completion))
            {
                _pendingRun = null;
                _pendingForce = false;
            }
        }
    }
}
