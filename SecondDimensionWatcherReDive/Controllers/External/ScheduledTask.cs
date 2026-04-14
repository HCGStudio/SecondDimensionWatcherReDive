namespace SecondDimensionWatcherReDive.Controllers.External;

internal sealed record ScheduledTask(
    string Id,
    string Interval,
    bool IsEnabled,
    DateTimeOffset? LastRunAt,
    bool IsRunning);
