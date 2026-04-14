namespace SecondDimensionWatcherReDive.Plugin;

public interface IPluginEventTrigger<in TParams>
{
    Task Invoke(TParams value, CancellationToken cancellationToken = default);
}
