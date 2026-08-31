namespace SecondDimensionWatcherReDive.Framework.DataRepository;

public enum MigrationExecutionStatus
{
    Pending = 0,
    Running = 1,
    Failed = 2,
    Completed = 3
}

/// <summary>
///     Durable, operator-visible state for a single version of a data migration.
/// </summary>
public sealed record MigrationExecution(
    string Key,
    int Version,
    MigrationExecutionStatus Status,
    string? Checkpoint,
    DateTimeOffset? StartedAt,
    DateTimeOffset? FinishedAt,
    DateTimeOffset UpdatedAt,
    int AttemptCount,
    string? LastErrorSummary);
