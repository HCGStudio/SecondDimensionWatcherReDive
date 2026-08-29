namespace SecondDimensionWatcherReDive.Controllers.External;

internal sealed record NotificationDeliveryItem(
    Guid Id,
    string Type,
    string Status,
    int AttemptCount,
    DateTimeOffset OccurredAt,
    DateTimeOffset? LastAttemptAt,
    DateTimeOffset? DeliveredAt,
    string? LastError);

internal sealed record TestNotificationResponse(Guid EventId);
