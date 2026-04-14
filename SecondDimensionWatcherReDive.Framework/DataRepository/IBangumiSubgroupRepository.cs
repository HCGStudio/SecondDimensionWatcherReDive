namespace SecondDimensionWatcherReDive.Framework.DataRepository;

public interface IBangumiSubgroupRepository
{
    Task<IList<BangumiSubgroup>> GetBySeasonBangumiIdAsync(Guid seasonBangumiId, CancellationToken cancellationToken);

    Task<BangumiSubgroup?> FindBySeasonBangumiAndSubgroupIdAsync(Guid seasonBangumiId, int mikanSubgroupId, CancellationToken cancellationToken);

    Task AddAsync(BangumiSubgroup subgroup, CancellationToken cancellationToken);

    Task UpdateAsync(BangumiSubgroup subgroup, CancellationToken cancellationToken);
}
