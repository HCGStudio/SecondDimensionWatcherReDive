using System.Text.Json.Serialization;

namespace SecondDimensionWatcherReDive.Framework.Notifications;

[JsonConverter(typeof(JsonStringEnumConverter<NotificationEventType>))]
public enum NotificationEventType
{
    [JsonStringEnumMemberName("releaseMatched")]
    ReleaseMatched,
    [JsonStringEnumMemberName("downloadPendingConfirmation")]
    DownloadPendingConfirmation,
    [JsonStringEnumMemberName("downloadCompleted")]
    DownloadCompleted,
    [JsonStringEnumMemberName("downloadFailed")]
    DownloadFailed,
    [JsonStringEnumMemberName("incidentOpened")]
    IncidentOpened,
    [JsonStringEnumMemberName("metadataNeedsReview")]
    MetadataNeedsReview,
    [JsonStringEnumMemberName("diskSpaceLow")]
    DiskSpaceLow,
    [JsonStringEnumMemberName("test")]
    Test
}

public sealed record NotificationEvent(
    NotificationEventType Type,
    string DeduplicationKey,
    string Title,
    string Body,
    string DeepLink,
    string? PayloadJson = null,
    DateTimeOffset? OccurredAt = null,
    Guid? Id = null);

public enum NotificationPublicationOutcome
{
    NotRequired,
    Persisted,
    Failed
}

public interface INotificationPublisher
{
    /// <summary>
    /// Distinguishes disabled/unsubscribed destinations from a persistence failure.
    /// Persisted includes an existing outbox entry for every currently eligible target.
    /// </summary>
    Task<NotificationPublicationOutcome> PublishDurablyAsync(
        NotificationEvent notificationEvent,
        CancellationToken cancellationToken);

    /// <summary>Returns true when at least one target is eligible and all eligible targets have durable outbox entries.</summary>
    Task<bool> EnsurePublishedAsync(NotificationEvent notificationEvent, CancellationToken cancellationToken);

    Task<bool> PublishAsync(
        NotificationEvent notificationEvent,
        CancellationToken cancellationToken);
}
