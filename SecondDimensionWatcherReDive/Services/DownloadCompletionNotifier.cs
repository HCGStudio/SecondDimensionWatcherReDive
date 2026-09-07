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
        await notificationPublisher.PublishAsync(new NotificationEvent(
            NotificationEventType.DownloadCompleted,
            $"download-completed:{info.Id}:{payload.DownloadAttemptId?.ToString() ?? "legacy"}",
            "Download completed",
            info.Title,
            "/downloaded",
            Id: eventId), cancellationToken);
    }
}
