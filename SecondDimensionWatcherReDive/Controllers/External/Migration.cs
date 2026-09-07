namespace SecondDimensionWatcherReDive.Controllers.External;

internal sealed record MigrationExecutionResponse(
    string Key,
    int Version,
    string Status,
    string? Checkpoint,
    DateTimeOffset? StartedAt,
    DateTimeOffset? FinishedAt,
    DateTimeOffset UpdatedAt,
    int AttemptCount,
    string? LastErrorSummary);

internal sealed record MigrationRetryResponse(
    bool IsSuccess,
    MigrationExecutionResponse? Execution,
    string? Error);
