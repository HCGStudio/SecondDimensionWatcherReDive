namespace SecondDimensionWatcherReDive.Framework.DataRepository;

public sealed record ScheduledTaskLeaseState(
    string TaskId,
    string? LeaseOwner,
    DateTimeOffset? LeaseExpiresAt,
    DateTimeOffset? LastStartedAt,
    DateTimeOffset? LastCompletedAt);
