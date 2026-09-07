namespace SecondDimensionWatcherReDive.Framework.DataRepository;

/// <summary>
///     Persists the lifecycle and resumable checkpoint of one versioned data migration.
/// </summary>
public interface IMigrationStateRepository
{
    Task<MigrationExecution?> FindAsync(
        string key,
        int version,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<MigrationExecution>> GetAllAsync(CancellationToken cancellationToken);

    Task<MigrationExecution> EnsurePendingAsync(
        string key,
        int version,
        DateTimeOffset now,
        CancellationToken cancellationToken);

    Task<MigrationExecution> MarkRunningAsync(
        string key,
        int version,
        DateTimeOffset now,
        CancellationToken cancellationToken);

    Task<MigrationExecution> SaveCheckpointAsync(
        string key,
        int version,
        string? checkpoint,
        DateTimeOffset now,
        CancellationToken cancellationToken);

    Task<MigrationExecution> MarkCompletedAsync(
        string key,
        int version,
        string? checkpoint,
        DateTimeOffset now,
        CancellationToken cancellationToken);

    Task<MigrationExecution> MarkFailedAsync(
        string key,
        int version,
        string? checkpoint,
        string errorSummary,
        DateTimeOffset now,
        CancellationToken cancellationToken);
}
