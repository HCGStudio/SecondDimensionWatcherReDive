namespace SecondDimensionWatcherReDive.Framework.Plugin;

/// <summary>
/// Represents an interface for registering plugin events.
/// </summary>
/// <typeparam name="TParam">The type of the event parameter.</typeparam>
public interface IPluginEventRegister<out TParam>
{
    /// <summary>
    /// Registers a plugin event and associates it with the specified action.
    /// </summary>
    /// <typeparam name="TParam">The type of the event parameter.</typeparam>
    /// <param name="action">The action to associate with the event.</param>
    public void Register(Func<TParam, CancellationToken, Task> action);
}
