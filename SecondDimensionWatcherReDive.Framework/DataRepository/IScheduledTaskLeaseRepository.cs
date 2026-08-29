namespace SecondDimensionWatcherReDive.Framework.DataRepository;

public interface IScheduledTaskLeaseRepository
{
    Task<bool> TryAcquireAsync(
        string taskId,
        string ownerId,
        DateTimeOffset now,
        DateTimeOffset leaseUntil,
        bool force,
        CancellationToken cancellationToken);

    Task<bool> RenewAsync(
        string taskId,
        string ownerId,
        DateTimeOffset now,
        DateTimeOffset leaseUntil,
        CancellationToken cancellationToken);

    Task CompleteAsync(
        string taskId,
        string ownerId,
        DateTimeOffset completedAt,
        DateTimeOffset leaseUntil,
        bool succeeded,
        string? error,
        CancellationToken cancellationToken);
}
