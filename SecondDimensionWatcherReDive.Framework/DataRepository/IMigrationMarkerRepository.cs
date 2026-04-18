namespace SecondDimensionWatcherReDive.Framework.DataRepository;

public interface IMigrationMarkerRepository
{
    Task<bool> ExistsAsync(string key, CancellationToken cancellationToken);

    Task SetAsync(string key, CancellationToken cancellationToken);
}
