namespace SecondDimensionWatcherReDive.Plugin;

public interface IPluginEventTrigger<in TParams>
{
    Task InvokeAsync(TParams value, CancellationToken cancellationToken);
}
