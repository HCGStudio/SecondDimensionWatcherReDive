using SecondDimensionWatcherReDive.Framework.DataRepository;
using SecondDimensionWatcherReDive.Framework.Notifications;

namespace SecondDimensionWatcherReDive.Utils.Notifications;

public sealed partial class NotificationPublisher(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    ILogger<NotificationPublisher> logger) : INotificationPublisher
{
    public async Task PublishAsync(
        NotificationEvent notificationEvent,
        CancellationToken cancellationToken)
    {
        if (!configuration.GetValue<bool>("Notifications:Webhook:Enabled"))
            return;
        if (notificationEvent.Type != NotificationEventType.Test
            && !SubscribedEvents(configuration["Notifications:Events"])
                .Contains(notificationEvent.Type))
            return;

        var occurredAt = notificationEvent.OccurredAt ?? DateTimeOffset.UtcNow;
        var message = new NotificationOutboxMessage(
            notificationEvent.Id ?? Guid.NewGuid(),
            notificationEvent.DeduplicationKey,
            notificationEvent.Type,
            Limit(notificationEvent.Title, 256),
            Limit(notificationEvent.Body, 2048),
            Limit(notificationEvent.DeepLink, 2048),
            notificationEvent.PayloadJson,
            occurredAt,
            NotificationDeliveryStatus.Pending,
            0,
            occurredAt,
            null,
            null,
            null);

        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var repository = scope.ServiceProvider
                .GetRequiredService<INotificationOutboxRepository>();
            if (!await repository.EnqueueAsync(message, cancellationToken))
                LogDuplicateSkipped(logger, notificationEvent.Type);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            // Notification persistence is deliberately isolated from the core operation.
            LogEnqueueFailed(logger, exception, notificationEvent.Type);
        }
    }

    internal static IReadOnlySet<NotificationEventType> SubscribedEvents(string? value)
    {
        var events = new HashSet<NotificationEventType>();
        foreach (var item in (value ?? string.Empty)
                     .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            if (Enum.TryParse<NotificationEventType>(item, true, out var type))
                events.Add(type);
        }
        return events;
    }

    private static string Limit(string value, int maxLength)
    {
        var normalized = string.IsNullOrWhiteSpace(value) ? "Notification" : value.Trim();
        return normalized.Length <= maxLength ? normalized : normalized[..maxLength];
    }

    [LoggerMessage(Level = LogLevel.Debug,
        Message = "Skipped duplicate notification {NotificationType}")]
    private static partial void LogDuplicateSkipped(
        ILogger logger,
        NotificationEventType notificationType);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Failed to enqueue notification {NotificationType}; the core operation remains successful")]
    private static partial void LogEnqueueFailed(
        ILogger logger,
        Exception exception,
        NotificationEventType notificationType);
}
