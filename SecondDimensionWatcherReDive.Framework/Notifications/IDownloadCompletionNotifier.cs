using SecondDimensionWatcherReDive.Framework.DataRepository;

namespace SecondDimensionWatcherReDive.Framework.Notifications;

/// <summary>
/// Optional notification effect in the durable download-completion workflow.
/// Implementations must treat <paramref name="eventId"/> as an idempotency key.
/// </summary>
public interface IDownloadCompletionNotifier
{
    Task NotifyAsync(
        Guid eventId,
        DownloadCompletionJobPayload payload,
        CancellationToken cancellationToken);
}
