using SecondDimensionWatcherReDive.Framework.Notifications;

namespace SecondDimensionWatcherReDive.Framework.DataRepository;

public enum NotificationDeliveryStatus
{
    Pending,
    Processing,
    Delivered,
    Failed
}

public sealed record NotificationOutboxMessage(
    Guid Id,
    string DeduplicationKey,
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
        DateTimeOffset now,
        DateTimeOffset leaseUntil,
        int take,
        CancellationToken cancellationToken);

    Task MarkDeliveredAsync(
        Guid id,
        DateTimeOffset deliveredAt,
        CancellationToken cancellationToken);

    Task MarkFailedAsync(
        Guid id,
        int attemptCount,
        DateTimeOffset attemptedAt,
        DateTimeOffset? nextAttemptAt,
        string error,
        CancellationToken cancellationToken);

    Task RescheduleAsync(
        Guid id,
        DateTimeOffset nextAttemptAt,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<NotificationOutboxMessage>> GetRecentAsync(
        int take,
        CancellationToken cancellationToken);
}
