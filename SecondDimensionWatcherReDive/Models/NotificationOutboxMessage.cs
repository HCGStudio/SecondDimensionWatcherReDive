using SecondDimensionWatcherReDive.Framework.DataRepository;
using SecondDimensionWatcherReDive.Framework.Notifications;

namespace SecondDimensionWatcherReDive.Models;

public sealed class NotificationOutboxMessage
{
    public Guid Id { get; set; }
    public string DeduplicationKey { get; set; } = string.Empty;
    public NotificationEventType Type { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public string DeepLink { get; set; } = string.Empty;
    public string? PayloadJson { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
    public NotificationDeliveryStatus Status { get; set; }
    public int AttemptCount { get; set; }
    public DateTimeOffset NextAttemptAt { get; set; }
    public DateTimeOffset? LastAttemptAt { get; set; }
    public DateTimeOffset? DeliveredAt { get; set; }
    public string? LastError { get; set; }
}
