using SecondDimensionWatcherReDive.Framework.Plugin;

namespace SecondDimensionWatcherReDive.Framework.PluginEvents;

public static class PluginEventExtension
{
    public static void RegisterEvent<TParam>(
        this IPluginServices pluginServices,
        string eventName,
        Func<TParam, Task> action)
    {
        pluginServices.GetRegister<TParam>(eventName).Register(action);
    }
}