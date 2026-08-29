using SecondDimensionWatcherReDive.Framework.Plugin;

namespace SecondDimensionWatcherReDive.Plugin;

public class PluginEvent<T> : IPluginEventRegister<T>, IPluginEventTrigger<T>
{
    private readonly List<Func<T, CancellationToken, Task>> _handlers = [];
    private readonly object _gate = new();
    private readonly TimeSpan _handlerTimeout;
    private readonly Action<Exception>? _onHandlerError;

    public PluginEvent(TimeSpan? handlerTimeout = null, Action<Exception>? onHandlerError = null)
    {
        _handlerTimeout = handlerTimeout ?? TimeSpan.FromSeconds(5);
        _onHandlerError = onHandlerError;
    }

    public void Register(Func<T, CancellationToken, Task> action)
    {
        ArgumentNullException.ThrowIfNull(action);
        lock (_gate) _handlers.Add(action);
    }

    public async Task InvokeAsync(T value, CancellationToken cancellationToken = default)
    {
        Func<T, CancellationToken, Task>[] handlers;
        lock (_gate) handlers = _handlers.ToArray();
        foreach (var handler in handlers)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var handlerCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            handlerCancellation.CancelAfter(_handlerTimeout);
            try
            {
                await handler(value, handlerCancellation.Token)
                    .WaitAsync(_handlerTimeout, cancellationToken);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                _onHandlerError?.Invoke(new TimeoutException(
                    $"Plugin event handler exceeded {_handlerTimeout.TotalMilliseconds:0} ms."));
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (TimeoutException exception)
            {
                _onHandlerError?.Invoke(exception);
            }
            catch (Exception exception)
            {
                _onHandlerError?.Invoke(exception);
            }
        }
    }
}
