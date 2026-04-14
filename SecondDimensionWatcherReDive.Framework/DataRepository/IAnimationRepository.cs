namespace SecondDimensionWatcherReDive.Framework.DataRepository;

public interface IAnimationRepository
{
    Task<Animation?> FindByTmdbIdAsync(string tmdbId, CancellationToken cancellationToken);

    Task AddAsync(Animation animation, CancellationToken cancellationToken);
}
