namespace SecondDimensionWatcherReDive.Framework.DataRepository;

/// <summary>
///     Determines what happens when a feed item matches its subscription automation policy.
/// </summary>
public enum SubscriptionAutomationMode
{
    NotifyOnly,
    ManualConfirm,
    AutoDownload
}
