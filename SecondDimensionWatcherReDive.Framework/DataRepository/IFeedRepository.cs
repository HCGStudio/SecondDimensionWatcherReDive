namespace SecondDimensionWatcherReDive.Framework.DataRepository;

public interface IFeedRepository
{
    Task<IReadOnlyList<Feed>> GetAllOrderedAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<string>> GetAllUrlsAsync(CancellationToken cancellationToken);

    Task<bool> ExistsByUrlAsync(string url, CancellationToken cancellationToken);

    Task<Feed?> FindByIdAsync(Guid id, CancellationToken cancellationToken);

    Task AddAsync(Feed feed, CancellationToken cancellationToken);

    Task RemoveAsync(Feed feed, CancellationToken cancellationToken);
}
