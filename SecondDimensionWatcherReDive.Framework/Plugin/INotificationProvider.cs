namespace SecondDimensionWatcherReDive.Framework.Plugin;

public sealed record PluginNotification(
    string Title,
    string Message,
    string Severity = "info",
    IReadOnlyDictionary<string, string>? Metadata = null);

public interface INotificationProvider
{
    string Name { get; }

    Task SendAsync(
        PluginNotification notification,
        CancellationToken cancellationToken);
}
