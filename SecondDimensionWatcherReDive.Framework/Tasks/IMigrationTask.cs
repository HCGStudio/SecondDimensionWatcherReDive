namespace SecondDimensionWatcherReDive.Framework.Tasks;

/// <summary>
///     Versioned data migration hosted by the startup migration runner. Implementations
///     must make every unit before a saved checkpoint idempotent so an interrupted
///     process can safely replay it.
/// </summary>
public interface IMigrationTask
{
    string Key { get; }

    int Version { get; }

    MigrationFailurePolicy FailurePolicy { get; }

    Task ExecuteAsync(
        MigrationExecutionContext context,
        CancellationToken cancellationToken);
}
