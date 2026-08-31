using SecondDimensionWatcherReDive.Framework.DataRepository;

namespace SecondDimensionWatcherReDive.Models;

public sealed class MigrationExecutionState
{
    public string Key { get; set; } = string.Empty;

    public int Version { get; set; }

    public MigrationExecutionStatus Status { get; set; }

    public string? Checkpoint { get; set; }

    public DateTimeOffset? StartedAt { get; set; }

    public DateTimeOffset? FinishedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public int AttemptCount { get; set; }

    public string? LastErrorSummary { get; set; }
}
