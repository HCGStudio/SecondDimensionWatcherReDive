using SecondDimensionWatcherReDive.Framework.Exceptions;
using SecondDimensionWatcherReDive.Framework.Plugin;

namespace SecondDimensionWatcherReDive.Plugin;

public class PluginServices(IServiceProvider serviceProvider) : IPluginServices
{
    private readonly Dictionary<string, object> _events = new();

    public IServiceProvider ServiceProvider => serviceProvider;

    public void AddEvent<TParam>(string eventName, PluginEvent<TParam> pluginEvent)
        => _events[eventName] = pluginEvent;

    public IPluginEventRegister<TParams> GetRegister<TParams>(string eventName)
    {
        if (!_events.TryGetValue(eventName, out var evt))
            throw new EventNotFoundException(eventName);
        return (IPluginEventRegister<TParams>)evt;
    }
}
