using SecondDimensionWatcherReDive.Framework.DataRepository;
using SecondDimensionWatcherReDive.Framework.Notifications;

namespace SecondDimensionWatcherReDive.Services;

internal sealed class NullDownloadCompletionNotifier : IDownloadCompletionNotifier
{
    public Task NotifyAsync(
        Guid eventId,
        DownloadCompletionJobPayload payload,
        CancellationToken cancellationToken) => Task.CompletedTask;
}
