namespace SecondDimensionWatcherReDive.Framework.Tasks;

public sealed class ScheduledTaskLeaseUnavailableException(Exception innerException)
    : Exception("The scheduled-task lease store is unavailable.", innerException);

public interface IScheduledTaskExecutionLease : IAsyncDisposable
{
    CancellationToken LeaseLostToken { get; }

    Task CompleteAsync(
        bool succeeded,
        string? error,
        CancellationToken cancellationToken);
}

public interface IScheduledTaskLeaseManager
{
    Task<IScheduledTaskExecutionLease?> TryAcquireAsync(
        string taskId,
        TimeSpan interval,
        bool force,
        CancellationToken cancellationToken);
}
