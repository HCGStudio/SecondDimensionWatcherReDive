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

public interface INotificationPublisher
{
    Task PublishAsync(
        NotificationEvent notificationEvent,
        CancellationToken cancellationToken);
}
