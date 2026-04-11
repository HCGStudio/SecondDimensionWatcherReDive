using SecondDimensionWatcherReDive.Framework.Plugin;

namespace SecondDimensionWatcherReDive.Plugin;

public class PluginEvent<T> : IPluginEventRegister<T>, IPluginEventTrigger<T>
{
    private readonly List<Func<T, Task>> _handlers = [];

    public void Register(Func<T, Task> action) => _handlers.Add(action);

    public async Task Invoke(T value)
    {
        foreach (var handler in _handlers)
            await handler(value);
    }
}
