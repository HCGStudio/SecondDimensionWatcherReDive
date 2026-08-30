namespace SecondDimensionWatcherReDive.Framework.DataRepository;

public interface IReadinessRepository
{
    Task<bool> CanConnectAsync(CancellationToken cancellationToken);
}
