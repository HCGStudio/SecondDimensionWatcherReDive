namespace SecondDimensionWatcherReDive.Framework.DataRepository;

/// <summary>
///     The persisted outcome of applying a subscription policy to a feed item.
/// </summary>
public enum SubscriptionAutomationDisposition
{
    Notified,
    PendingConfirmation,
    AutoDownloadQueued,
    AutoDownloadFailed,
    ManualDownloadQueued,
    DownloadCompleted,
    DownloadCancelled
}
