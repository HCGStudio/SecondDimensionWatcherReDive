using SecondDimensionWatcherReDive.Framework.DataRepository;
using SecondDimensionWatcherReDive.Framework.Notifications;

namespace SecondDimensionWatcherReDive.Services;

internal sealed class DownloadCompletionNotifier(
    IAnimationInfoRepository animationInfoRepository,
    INotificationPublisher notificationPublisher) : IDownloadCompletionNotifier
{
    public async Task NotifyAsync(
        Guid eventId,
        DownloadCompletionJobPayload payload,
        CancellationToken cancellationToken)
    {
        var info = await animationInfoRepository.FindByIdAsync(payload.ItemId, cancellationToken);
        if (info is null) return;
        var outcome = await notificationPublisher.PublishDurablyAsync(new NotificationEvent(
            NotificationEventType.DownloadCompleted,
            $"download-completed:{info.Id}:{payload.DownloadAttemptId?.ToString() ?? "legacy"}",
            "Download completed",
            info.Title,
            "/downloaded",
            Id: eventId), cancellationToken);
        if (outcome == NotificationPublicationOutcome.Failed)
            throw new InvalidOperationException("Download completion notification could not be persisted for every destination.");
    }
}
