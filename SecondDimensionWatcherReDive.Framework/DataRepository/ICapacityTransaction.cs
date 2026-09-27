namespace SecondDimensionWatcherReDive.Framework.DataRepository;

public interface ICapacityTransaction : IAsyncDisposable
{
    Task CommitAsync(CancellationToken cancellationToken);
}
