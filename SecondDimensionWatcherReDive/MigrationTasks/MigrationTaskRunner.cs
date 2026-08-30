using SecondDimensionWatcherReDive.Framework.DataRepository;
using SecondDimensionWatcherReDive.Framework.Tasks;

namespace SecondDimensionWatcherReDive.MigrationTasks;

/// <summary>
///     Runs versioned data migrations under the database-wide lease acquired by
///     the caller. Every lifecycle transition is persisted before the next phase.
/// </summary>
public sealed partial class MigrationTaskRunner(
    IServiceScopeFactory scopeFactory,
    IEnumerable<IMigrationTask> migrations,
    TimeProvider timeProvider,
    ILogger<MigrationTaskRunner> logger)
{
    private const int ErrorSummaryLimit = 4096;
    private static readonly TimeSpan FailureWriteTimeout = TimeSpan.FromSeconds(10);
    private readonly IReadOnlyList<IMigrationTask> _migrations = migrations.ToList();

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        ValidateRegistrations();
        foreach (var migration in _migrations)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await RunAsync(migration, cancellationToken);
        }
    }

    public async Task<bool> HasPendingAsync(CancellationToken cancellationToken)
    {
        ValidateRegistrations();
        foreach (var migration in _migrations)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var state = await WithRepositoryAsync(
                repository => repository.FindAsync(
                    migration.Key,
                    migration.Version,
                    cancellationToken));
            if (state?.Status != MigrationExecutionStatus.Completed) return true;
        }
        return false;
    }

    public async Task<MigrationExecution> RunAsync(
        IMigrationTask migration,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(migration);
        ValidateMigration(migration);

        var state = await WithRepositoryAsync(
            repository => repository.EnsurePendingAsync(
                migration.Key,
                migration.Version,
                timeProvider.GetUtcNow(),
                cancellationToken));
        if (state.Status == MigrationExecutionStatus.Completed)
        {
            LogSkipped(logger, migration.Key, migration.Version);
            return state;
        }

        if (state.Status is MigrationExecutionStatus.Running or MigrationExecutionStatus.Failed)
            LogResuming(
                logger,
                migration.Key,
                migration.Version,
                state.Status,
                state.Checkpoint);

        state = await WithRepositoryAsync(
            repository => repository.MarkRunningAsync(
                migration.Key,
                migration.Version,
                timeProvider.GetUtcNow(),
                cancellationToken));
        var context = new MigrationExecutionContext(
            state.Checkpoint,
            async (checkpoint, token) =>
            {
                await WithRepositoryAsync(
                    repository => repository.SaveCheckpointAsync(
                        migration.Key,
                        migration.Version,
                        checkpoint,
                        timeProvider.GetUtcNow(),
                        token));
            });

        LogStart(logger, migration.Key, migration.Version, state.AttemptCount);
        try
        {
            await migration.ExecuteAsync(context, cancellationToken);
        }
        catch (OperationCanceledException exception)
        {
            await MarkFailedAfterExceptionAsync(migration, context.Checkpoint, exception);
            LogCancelled(logger, migration.Key, migration.Version, context.Checkpoint);
            throw;
        }
        catch (Exception exception)
        {
            var failedState = await MarkFailedAfterExceptionAsync(
                migration,
                context.Checkpoint,
                exception);
            LogFailed(logger, exception, migration.Key, migration.Version, context.Checkpoint);
            if (migration.FailurePolicy == MigrationFailurePolicy.BlockStartup)
                throw new MigrationTaskFailedException(
                    migration.Key,
                    migration.Version,
                    exception);
            return failedState;
        }

        var completed = await WithRepositoryAsync(
            repository => repository.MarkCompletedAsync(
                migration.Key,
                migration.Version,
                context.Checkpoint,
                timeProvider.GetUtcNow(),
                cancellationToken));
        LogComplete(logger, migration.Key, migration.Version, context.Checkpoint);
        return completed;
    }

    public IMigrationTask? Find(string key, int version) =>
        _migrations.SingleOrDefault(migration =>
            migration.Key == key && migration.Version == version);

    private async Task<MigrationExecution> MarkFailedAfterExceptionAsync(
        IMigrationTask migration,
        string? checkpoint,
        Exception exception)
    {
        using var timeout = new CancellationTokenSource(FailureWriteTimeout);
        try
        {
            return await WithRepositoryAsync(
                repository => repository.MarkFailedAsync(
                    migration.Key,
                    migration.Version,
                    checkpoint,
                    Summarize(exception),
                    timeProvider.GetUtcNow(),
                    timeout.Token));
        }
        catch (Exception persistenceException)
        {
            LogFailureStateWriteFailed(
                logger,
                persistenceException,
                migration.Key,
                migration.Version);
            throw new MigrationStatePersistenceException(
                migration.Key,
                migration.Version,
                exception,
                persistenceException);
        }
    }

    private async Task<T> WithRepositoryAsync<T>(
        Func<IMigrationStateRepository, Task<T>> operation)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var repository = scope.ServiceProvider.GetRequiredService<IMigrationStateRepository>();
        return await operation(repository);
    }

    private void ValidateRegistrations()
    {
        foreach (var migration in _migrations) ValidateMigration(migration);
        var duplicate = _migrations
            .GroupBy(migration => (migration.Key, migration.Version))
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
            throw new InvalidOperationException(
                $"Migration '{duplicate.Key.Key}' version {duplicate.Key.Version} is registered more than once.");
    }

    private static void ValidateMigration(IMigrationTask migration)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(migration.Key);
        if (migration.Key.Length > 256)
            throw new InvalidOperationException("Migration keys cannot exceed 256 characters.");
        if (migration.Version <= 0)
            throw new InvalidOperationException(
                $"Migration '{migration.Key}' has invalid version {migration.Version}.");
    }

    private static string Summarize(Exception exception)
    {
        var summary = $"{exception.GetType().Name}: {exception.Message}";
        return summary.Length <= ErrorSummaryLimit
            ? summary
            : summary[..ErrorSummaryLimit];
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Skipping migration {Key} v{Version}: already completed")]
    private static partial void LogSkipped(ILogger logger, string key, int version);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Resuming migration {Key} v{Version} from {Status} at checkpoint {Checkpoint}")]
    private static partial void LogResuming(
        ILogger logger,
        string key,
        int version,
        MigrationExecutionStatus status,
        string? checkpoint);

    [LoggerMessage(Level = LogLevel.Information, Message = "Running migration {Key} v{Version}, attempt {AttemptCount}")]
    private static partial void LogStart(ILogger logger, string key, int version, int attemptCount);

    [LoggerMessage(Level = LogLevel.Information, Message = "Migration {Key} v{Version} completed at checkpoint {Checkpoint}")]
    private static partial void LogComplete(ILogger logger, string key, int version, string? checkpoint);

    [LoggerMessage(Level = LogLevel.Error, Message = "Migration {Key} v{Version} failed at checkpoint {Checkpoint}")]
    private static partial void LogFailed(
        ILogger logger,
        Exception exception,
        string key,
        int version,
        string? checkpoint);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Migration {Key} v{Version} was cancelled at checkpoint {Checkpoint}")]
    private static partial void LogCancelled(
        ILogger logger,
        string key,
        int version,
        string? checkpoint);

    [LoggerMessage(Level = LogLevel.Critical, Message = "Could not persist failed state for migration {Key} v{Version}")]
    private static partial void LogFailureStateWriteFailed(
        ILogger logger,
        Exception exception,
        string key,
        int version);
}

public sealed class MigrationTaskFailedException(
    string key,
    int version,
    Exception innerException)
    : Exception($"Migration '{key}' version {version} failed and blocks startup.", innerException)
{
    public string Key { get; } = key;

    public int Version { get; } = version;
}

public sealed class MigrationStatePersistenceException(
    string key,
    int version,
    Exception migrationException,
    Exception persistenceException)
    : AggregateException(
        $"Migration '{key}' version {version} failed and its failed state could not be persisted.",
        migrationException,
        persistenceException);
