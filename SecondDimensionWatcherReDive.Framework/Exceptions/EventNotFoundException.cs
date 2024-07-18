namespace SecondDimensionWatcherReDive.Framework.Exceptions;

/// <summary>
/// Exception thrown when an event is not found.
/// </summary>
public class EventNotFoundException(string eventName) : Exception
{
    /// <inheritdoc />
    public override string Message => $"The event `{eventName}` is not supported.";

    /// <summary>
    /// Represents the name of an event.
    /// </summary>
    /// <remarks>
    /// This property is used to store the name of an event.
    /// </remarks>
    public string EventName => eventName;
}