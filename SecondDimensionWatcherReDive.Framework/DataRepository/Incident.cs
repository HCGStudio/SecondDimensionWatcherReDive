namespace SecondDimensionWatcherReDive.Framework.DataRepository;

public enum IncidentType
{
    FeedFailure,
    DownloadStalled,
    AiFailure,
    FileMappingFailure,
    DiskSpaceLow
}

public enum IncidentSeverity
{
    Warning,
    Error,
    Critical
}

public sealed record Incident(
    Guid Id,
    string Fingerprint,
    IncidentType Type,
    IncidentSeverity Severity,
    string Title,
    string Detail,
    string SourceId,
    DateTimeOffset DetectedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? ResolvedAt,
    int RetryCount,
    DateTimeOffset? LastRetryAt,
    string? LastRetryError,
    int Occurrence = 1);

public sealed record IncidentPage(
    IReadOnlyList<Incident> Items,
    int TotalCount,
    int OpenCount,
    IReadOnlyDictionary<IncidentType, int> OpenCountsByType);
