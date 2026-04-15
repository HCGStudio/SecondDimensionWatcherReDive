using SecondDimensionWatcherReDive.Framework.Plugin;

namespace SecondDimensionWatcherReDive.Plugin;

public class PluginEvent<T> : IPluginEventRegister<T>, IPluginEventTrigger<T>
{
    private readonly List<Func<T, CancellationToken, Task>> _handlers = [];

    public void Register(Func<T, CancellationToken, Task> action) => _handlers.Add(action);

    public async Task InvokeAsync(T value, CancellationToken cancellationToken = default)
    {
        foreach (var handler in _handlers)
            await handler(value, cancellationToken);
    }
}
