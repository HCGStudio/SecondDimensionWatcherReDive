namespace SecondDimensionWatcherReDive.Framework.Tasks;

public interface IScheduledTask
{
    string Name { get; }
    string Description { get; }
    TimeSpan Interval { get; }
    bool IsEnabled { get; }
    DateTimeOffset? LastRunAt { get; }
    bool IsRunning { get; }
    Task RunNowAsync(CancellationToken cancellationToken);
}
