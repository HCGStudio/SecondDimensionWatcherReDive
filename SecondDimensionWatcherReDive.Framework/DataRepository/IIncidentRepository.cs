namespace SecondDimensionWatcherReDive.Framework.DataRepository;

public interface IIncidentRepository
{
    Task<IncidentPage> GetPageAsync(
        IncidentType? type,
        bool includeResolved,
        int skip,
        int take,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<Incident>> GetOpenAsync(
        IncidentType? type,
        CancellationToken cancellationToken);

    Task<Incident?> FindByIdAsync(Guid id, CancellationToken cancellationToken);

    Task<Incident> UpsertAsync(Incident incident, CancellationToken cancellationToken);

    Task<Incident?> ResolveByFingerprintAsync(
        string fingerprint,
        DateTimeOffset resolvedAt,
        CancellationToken cancellationToken);

    Task<Incident?> RecordRetryAsync(
        Guid id,
        DateTimeOffset retriedAt,
        string? error,
        bool resolve,
        CancellationToken cancellationToken);
}
