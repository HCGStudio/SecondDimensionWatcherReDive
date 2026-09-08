namespace SecondDimensionWatcherReDive.Framework.DataRepository;

public sealed record DownloadCapacityEntry(
    Guid ItemId, Guid? DownloadAttemptId, string Title, string Hash, long? ExpectedBytes,
    long RemainingBytes, string State, bool Paused, string Reason,
    DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);

public interface ICapacityTransaction : IAsyncDisposable
{
    Task CommitAsync(CancellationToken cancellationToken);
}

/// <summary>All budget mutations run under the same transaction lock across replicas.</summary>
public interface IDownloadCapacityRepository
{
    Task<ICapacityTransaction> BeginAsync(CancellationToken cancellationToken);
    Task RecoverTrackedAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<DownloadCapacityEntry>> ListAsync(CancellationToken cancellationToken);
    Task SaveAsync(DownloadCapacityEntry entry, CancellationToken cancellationToken);
    Task RemoveAsync(Guid itemId, CancellationToken cancellationToken);
}
