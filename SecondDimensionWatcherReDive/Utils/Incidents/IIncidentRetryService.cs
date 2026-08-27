using SecondDimensionWatcherReDive.Framework.DataRepository;

namespace SecondDimensionWatcherReDive.Utils.Incidents;

public sealed record IncidentRetryResult(
    Guid IncidentId,
    string Status,
    bool IsSuccess,
    Incident? Incident,
    string? Error);

public sealed record IncidentRetryBatchResult(
    int Attempted,
    int Succeeded,
    int Failed,
    IReadOnlyList<IncidentRetryResult> Results);

public interface IIncidentRetryService
{
    Task<IncidentRetryResult?> RetryAsync(Guid id, CancellationToken cancellationToken);

    Task<IncidentRetryBatchResult> RetryAllAsync(CancellationToken cancellationToken);
}
