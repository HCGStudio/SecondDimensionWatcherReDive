using SecondDimensionWatcherReDive.Framework.DataRepository;
using SecondDimensionWatcherReDive.Framework.Tasks;

namespace SecondDimensionWatcherReDive.MigrationTasks;

public sealed class MigrationAdministrationService(
    IMigrationLock migrationLock,
    IMigrationStateRepository stateRepository,
    MigrationTaskRunner runner)
{
    public async Task<MigrationRetryResult> RetryAsync(
        string key,
        int version,
        CancellationToken cancellationToken)
    {
        var migration = runner.Find(key, version);
        if (migration is null)
            return new MigrationRetryResult(MigrationRetryStatus.NotFound, null, null);

        await using var lease = await migrationLock.AcquireAsync(cancellationToken);
        var state = await stateRepository.FindAsync(key, version, cancellationToken);
        if (state is null)
            return new MigrationRetryResult(MigrationRetryStatus.NotFound, null, null);
        if (state.Status != MigrationExecutionStatus.Failed)
            return new MigrationRetryResult(
                MigrationRetryStatus.NotFailed,
                state,
                "Only a failed migration can be manually retried.");

        try
        {
            var completed = await runner.RunAsync(migration, cancellationToken);
            return completed.Status == MigrationExecutionStatus.Completed
                ? new MigrationRetryResult(MigrationRetryStatus.Completed, completed, null)
                : new MigrationRetryResult(
                    MigrationRetryStatus.Failed,
                    completed,
                    completed.LastErrorSummary);
        }
        catch (MigrationTaskFailedException)
        {
            var failed = await stateRepository.FindAsync(key, version, cancellationToken);
            return new MigrationRetryResult(
                MigrationRetryStatus.Failed,
                failed,
                failed?.LastErrorSummary);
        }
    }
}

public enum MigrationRetryStatus
{
    NotFound,
    NotFailed,
    Failed,
    Completed
}

public sealed record MigrationRetryResult(
    MigrationRetryStatus Status,
    MigrationExecution? Execution,
    string? Error);
