namespace SecondDimensionWatcherReDive.Framework.DataRepository;

public sealed record WatchlistEpisode(Guid AnimationInfoId, string Title, int? Season, int? Episode,
    DateTimeOffset PublishedAt, string Availability, string? Path, double PositionSeconds);
public sealed record WatchlistEntry(Guid Id, string? TmdbId, int? MikanId, string Title, string Status,
    int? DayOfWeek, DateTimeOffset UpdatedAt, IReadOnlyList<WatchlistEpisode> Episodes);
public sealed record WatchlistUpdate(Guid? Id, string? TmdbId, int? MikanId, string Title, string Status,
    bool TmdbIdSpecified, bool MikanIdSpecified);

public interface IWatchlistRepository
{
    Task<IReadOnlyList<WatchlistEntry>> GetAsync(Guid profileId, DateTimeOffset weekStart, DateTimeOffset weekEnd, CancellationToken cancellationToken);
    Task<Guid> UpsertAsync(Guid profileId, WatchlistUpdate update, CancellationToken cancellationToken);
    Task<bool> DeleteAsync(Guid profileId, Guid id, CancellationToken cancellationToken);
}
