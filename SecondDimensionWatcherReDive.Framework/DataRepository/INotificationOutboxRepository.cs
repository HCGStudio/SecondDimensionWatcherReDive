using SecondDimensionWatcherReDive.Framework.Notifications;

namespace SecondDimensionWatcherReDive.Framework.DataRepository;

public enum NotificationDeliveryStatus
{
    Pending,
    Processing,
    Delivered,
    Failed
}

public enum NotificationChannel
{
    Webhook,
    WebPush
}

public sealed record NotificationOutboxMessage(
    Guid Id,
    Guid EventId,
    string DeduplicationKey,
    NotificationChannel Channel,
    Guid? WebPushSubscriptionId,
    NotificationEventType Type,
    string Title,
    string Body,
    string DeepLink,
    string? PayloadJson,
    DateTimeOffset OccurredAt,
    NotificationDeliveryStatus Status,
    int AttemptCount,
    DateTimeOffset NextAttemptAt,
    DateTimeOffset? LastAttemptAt,
    DateTimeOffset? DeliveredAt,
    string? LastError);

public interface INotificationOutboxRepository
{
    Task<bool> EnqueueAsync(
        NotificationOutboxMessage message,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<NotificationOutboxMessage>> ClaimDueAsync(
        TimeSpan leaseDuration,
        int take,
        CancellationToken cancellationToken);

    Task<bool> MarkDeliveredAsync(
        Guid id,
        DateTimeOffset expectedLeaseUntil,
        DateTimeOffset deliveredAt,
        CancellationToken cancellationToken);

    Task<bool> MarkFailedAsync(
        Guid id,
        DateTimeOffset expectedLeaseUntil,
        int attemptCount,
        DateTimeOffset attemptedAt,
        DateTimeOffset? nextAttemptAt,
        string error,
        CancellationToken cancellationToken);

    Task<bool> RescheduleAsync(
        Guid id,
        DateTimeOffset expectedLeaseUntil,
        DateTimeOffset nextAttemptAt,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<NotificationOutboxMessage>> GetRecentAsync(
        int take,
        CancellationToken cancellationToken);
}
