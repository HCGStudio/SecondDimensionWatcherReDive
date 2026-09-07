namespace SecondDimensionWatcherReDive.Framework.Tasks;

/// <summary>
///     Cross-process lease covering schema and data migrations for one database.
/// </summary>
public interface IMigrationLock
{
    Task<IMigrationLockLease> AcquireAsync(CancellationToken cancellationToken);
}

public interface IMigrationLockLease : IAsyncDisposable;
