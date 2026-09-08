namespace SecondDimensionWatcherReDive.Framework.Plugin;

public sealed record PluginNotification(
    string Title,
    string Message,
    string Severity = "info",
    IReadOnlyDictionary<string, string>? Metadata = null)
{
    /// <summary>Stable logical event identity. Providers must deduplicate external side effects by this value.</summary>
    public Guid? EventId { get; init; }
}

public interface INotificationProvider
{
    string Name { get; }

    Task SendAsync(
        PluginNotification notification,
        CancellationToken cancellationToken);
}
