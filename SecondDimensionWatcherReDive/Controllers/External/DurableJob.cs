namespace SecondDimensionWatcherReDive.Controllers.External;

public sealed record DurableJobItem(
    Guid Id,
    string Type,
    string Status,
    string Stage,
    int AttemptCount,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset NextAttemptAt,
    DateTimeOffset? LastAttemptAt,
    DateTimeOffset? CompletedAt,
    string? LastError);

public sealed record DurableJobListResponse(
    IReadOnlyList<DurableJobItem> Items,
    int TotalCount);

public sealed record DurableJobMutationRequest(IReadOnlyList<Guid> Ids);

public sealed record DurableJobMutationResponse(int AffectedCount);
