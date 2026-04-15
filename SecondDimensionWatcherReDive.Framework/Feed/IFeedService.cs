namespace SecondDimensionWatcherReDive.Framework.Feed;

/// <summary>
///     Interface for Feed Service.
/// </summary>
public interface IFeedService
{
    /// <summary>
    ///     Synchronizes the given resource.
    /// </summary>
    /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation.</param>
    /// <returns>A collection of AnimationAddRequest objects.</returns>
    public Task<ICollection<AnimationAddRequest>> SyncAsync(CancellationToken cancellationToken);
}