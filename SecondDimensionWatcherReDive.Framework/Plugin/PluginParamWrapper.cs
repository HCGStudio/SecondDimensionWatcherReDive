namespace SecondDimensionWatcherReDive.Framework.Plugin;

public class PluginParamWrapper<T>(T value)
{
    public T Value { get; set; } = value;
}