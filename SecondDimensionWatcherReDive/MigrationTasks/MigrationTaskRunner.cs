using SecondDimensionWatcherReDive.Framework.DataRepository;
using SecondDimensionWatcherReDive.Framework.Tasks;

namespace SecondDimensionWatcherReDive.MigrationTasks;

/// <summary>
///     Runs all registered <see cref="IMigrationTask" /> migrations once,
///     gated by <see cref="IMigrationMarkerRepository" />. Invoked synchronously
///     during application startup before the host begins serving so that no
///     request, scheduled task, or background channel processor sees a
///     half-migrated state.
/// </summary>
public partial class MigrationTaskRunner(
    IServiceScopeFactory scopeFactory,
    IEnumerable<IMigrationTask> migrations,
    ILogger<MigrationTaskRunner> logger)
{
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        foreach (var migration in migrations)
        {
            cancellationToken.ThrowIfCancellationRequested();

            await using var scope = scopeFactory.CreateAsyncScope();
            var markerRepository = scope.ServiceProvider.GetRequiredService<IMigrationMarkerRepository>();

            if (await markerRepository.ExistsAsync(migration.Key, cancellationToken))
            {
                LogSkipped(logger, migration.Key);
                continue;
            }

            LogStart(logger, migration.Key);
            try
            {
                await migration.ExecuteAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                LogFailed(logger, ex, migration.Key);
                throw;
            }

            await markerRepository.SetAsync(migration.Key, cancellationToken);
            LogComplete(logger, migration.Key);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Skipping migration {Key}: already applied")]
    private static partial void LogSkipped(ILogger logger, string key);

    [LoggerMessage(Level = LogLevel.Information, Message = "Running migration {Key}")]
    private static partial void LogStart(ILogger logger, string key);

    [LoggerMessage(Level = LogLevel.Information, Message = "Migration {Key} completed")]
    private static partial void LogComplete(ILogger logger, string key);

    [LoggerMessage(Level = LogLevel.Error, Message = "Migration {Key} failed; aborting startup")]
    private static partial void LogFailed(ILogger logger, Exception ex, string key);
}
