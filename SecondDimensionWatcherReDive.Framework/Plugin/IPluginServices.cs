using SecondDimensionWatcherReDive.Framework.Exceptions;

namespace SecondDimensionWatcherReDive.Framework.Plugin;

/// <summary>
/// Represents a service interface for plugin-related operations.
/// </summary>
public interface IPluginServices
{
    /// <summary>
    /// Retrieves the event register for the specified event name.
    /// </summary>
    /// <typeparam name="TParams">The type of the event parameter.</typeparam>
    /// <param name="eventName">The name of the event.</param>
    /// <returns>The event register associated with the specified event name.</returns>
    /// <exception cref="EventNotFoundException">Thrown when no event register can be found for the specified event name.</exception>
    /// <exception cref="InvalidCastException">Thrown when TParams type is incorrect for the specified event name.</exception>
    IPluginEventRegister<TParams> GetRegister<TParams>(string eventName);

    /// <summary>
    /// Represents a service provider for plugin-related operations.
    /// </summary>
    IServiceProvider ServiceProvider { get; }
}
