namespace SecondDimensionWatcherReDive.Attributes;

[AttributeUsage(AttributeTargets.All)]
public class PluginEventRegisterAttribute<TParam>(string eventName) : Attribute
{
    public string EventName { get; } = eventName;
}
