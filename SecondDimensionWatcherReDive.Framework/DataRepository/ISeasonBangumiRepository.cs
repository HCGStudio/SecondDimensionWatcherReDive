namespace SecondDimensionWatcherReDive.Framework.DataRepository;

public interface ISeasonBangumiRepository
{
    Task<IReadOnlyList<SeasonBangumi>> GetAllOrderedByDayAndTitleAsync(CancellationToken cancellationToken);

    Task<IList<SeasonBangumi>> GetAllAsync(CancellationToken cancellationToken);

    Task<SeasonBangumi?> FindByMikanIdAsync(int mikanId, CancellationToken cancellationToken);

    Task<DateTimeOffset?> GetLatestScrapedAtAsync(CancellationToken cancellationToken);

    Task AddAsync(SeasonBangumi bangumi, CancellationToken cancellationToken);

    Task UpdateAsync(SeasonBangumi bangumi, CancellationToken cancellationToken);

    Task RemoveRangeAsync(IEnumerable<SeasonBangumi> bangumis, CancellationToken cancellationToken);
}
