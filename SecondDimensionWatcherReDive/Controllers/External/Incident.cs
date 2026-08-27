namespace SecondDimensionWatcherReDive.Controllers.External;

internal sealed record IncidentItem(
    Guid Id,
    string Type,
    string Severity,
    string Title,
    string Detail,
    string? SourceId,
    DateTimeOffset DetectedAt,
    DateTimeOffset UpdatedAt,
    int RetryCount,
    DateTimeOffset? LastRetryAt,
    string? LastRetryError,
    DateTimeOffset? ResolvedAt,
    bool CanRetry);

internal sealed record IncidentListResponse(
    List<IncidentItem> Items,
    int TotalCount,
    int OpenCount,
    Dictionary<string, int> CountsByType);

internal sealed record IncidentRetryError(
    Guid IncidentId,
    bool Success,
    string? Error);

internal sealed record IncidentRetryBatchResponse(
    int Attempted,
    int Succeeded,
    int Failed,
    List<IncidentRetryError> Results);
