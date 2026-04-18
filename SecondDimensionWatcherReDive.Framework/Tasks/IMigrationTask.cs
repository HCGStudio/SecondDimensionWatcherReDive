namespace SecondDimensionWatcherReDive.Framework.Tasks;

/// <summary>
///     One-shot data migration. Each implementation runs at most once per
///     database (tracked via <c>MigrationMarkers</c>) and is hosted by the
///     migration runner during application startup, not the scheduled-task
///     infrastructure.
/// </summary>
public interface IMigrationTask
{
    string Key { get; }

    Task ExecuteAsync(CancellationToken cancellationToken);
}
