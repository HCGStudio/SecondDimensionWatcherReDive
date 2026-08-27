namespace SecondDimensionWatcherReDive.Framework.Feed;

/// <summary>
/// Reads the releases currently exposed by one subscription feed.
/// </summary>
public interface ISubscriptionFeedReader
{
    Task<IReadOnlyList<AnimationAddRequest>> ReadAsync(
        string feedUrl,
        Guid? feedId,
        CancellationToken cancellationToken);
}
