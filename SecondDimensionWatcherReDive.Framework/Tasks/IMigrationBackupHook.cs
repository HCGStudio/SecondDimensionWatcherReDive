namespace SecondDimensionWatcherReDive.Framework.Tasks;

/// <summary>
///     Optional operator-configured hook invoked under the migration lease before
///     schema or data migrations run.
/// </summary>
public interface IMigrationBackupHook
{
    Task ExecuteAsync(CancellationToken cancellationToken);
}
