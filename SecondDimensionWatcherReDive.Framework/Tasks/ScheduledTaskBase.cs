using System.Threading.Channels;

namespace SecondDimensionWatcherReDive.Framework.Tasks;

public abstract class ScheduledTaskBase : IScheduledTask
{
    private readonly Channel<TaskCompletionSource> _runQueue =
        Channel.CreateUnbounded<TaskCompletionSource>(
            new UnboundedChannelOptions { SingleReader = true });

    private volatile bool _isRunning;
    private DateTimeOffset? _lastRunAt;

    public abstract string Id { get; }
    public abstract TimeSpan Interval { get; }
    public virtual bool IsEnabled => true;
    public DateTimeOffset? LastRunAt => _lastRunAt;
    public bool IsRunning => _isRunning;

    public async Task RunNowAsync(CancellationToken cancellationToken)
    {
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        await using var registration = cancellationToken.Register(
            () => tcs.TrySetCanceled(cancellationToken));

        await _runQueue.Writer.WriteAsync(tcs, cancellationToken);
        await tcs.Task;
    }

    /// <summary>
    ///     Enqueues a run request without waiting for completion.
    /// </summary>
    public void Enqueue()
    {
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _runQueue.Writer.TryWrite(tcs);
    }

    /// <summary>
    ///     Sequentially processes queued run requests. Called by the hosting
    ///     BackgroundService; runs for the lifetime of the host.
    /// </summary>
    public async Task ProcessQueueAsync(CancellationToken cancellationToken)
    {
        await foreach (var tcs in _runQueue.Reader.ReadAllAsync(cancellationToken))
        {
            if (tcs.Task.IsCanceled) continue;

            _isRunning = true;
            try
            {
                await ExecuteTaskAsync(cancellationToken);
                _lastRunAt = DateTimeOffset.UtcNow;
                tcs.TrySetResult();
            }
            catch (OperationCanceledException ex)
            {
                tcs.TrySetCanceled(ex.CancellationToken);
            }
            catch (Exception ex)
            {
                tcs.TrySetException(ex);
            }
            finally
            {
                _isRunning = false;
            }
        }
    }

    protected abstract Task ExecuteTaskAsync(CancellationToken cancellationToken);
}
