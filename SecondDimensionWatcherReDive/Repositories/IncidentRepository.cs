using Microsoft.EntityFrameworkCore;
using Npgsql;
using SecondDimensionWatcherReDive.Framework.DataRepository;
using IncidentEntity = SecondDimensionWatcherReDive.Models.Incident;

namespace SecondDimensionWatcherReDive.Repositories;

public sealed class IncidentRepository(Models.ApplicationContext context) : IIncidentRepository
{
    public async Task<IncidentPage> GetPageAsync(
        IncidentType? type,
        bool includeResolved,
        int skip,
        int take,
        CancellationToken cancellationToken)
    {
        var query = context.Incidents.AsNoTracking().AsQueryable();
        if (type.HasValue) query = query.Where(incident => incident.Type == type.Value);
        if (!includeResolved) query = query.Where(incident => incident.ResolvedAt == null);

        var totalCount = await query.CountAsync(cancellationToken);
        var entities = await query
            .OrderByDescending(incident => incident.UpdatedAt)
            .ThenByDescending(incident => incident.DetectedAt)
            .Skip(skip)
            .Take(take)
            .ToListAsync(cancellationToken);

        var openCount = await context.Incidents
            .CountAsync(incident => incident.ResolvedAt == null, cancellationToken);
        var counts = await context.Incidents
            .Where(incident => incident.ResolvedAt == null)
            .GroupBy(incident => incident.Type)
            .Select(group => new { Type = group.Key, Count = group.Count() })
            .ToDictionaryAsync(item => item.Type, item => item.Count, cancellationToken);

        return new IncidentPage(
            entities.Select(ToRecord).ToList(),
            totalCount,
            openCount,
            counts);
    }

    public async Task<IReadOnlyList<Incident>> GetOpenAsync(
        IncidentType? type,
        CancellationToken cancellationToken)
    {
        var query = context.Incidents
            .AsNoTracking()
            .Where(incident => incident.ResolvedAt == null);
        if (type.HasValue) query = query.Where(incident => incident.Type == type.Value);

        return (await query
                .OrderBy(incident => incident.DetectedAt)
                .ToListAsync(cancellationToken))
            .Select(ToRecord)
            .ToList();
    }

    public async Task<Incident?> FindByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        var entity = await context.Incidents
            .AsNoTracking()
            .FirstOrDefaultAsync(incident => incident.Id == id, cancellationToken);
        return entity is null ? null : ToRecord(entity);
    }

    public async Task<Incident> UpsertAsync(Incident incident, CancellationToken cancellationToken)
    {
        var entity = await context.Incidents
            .FirstOrDefaultAsync(candidate => candidate.Fingerprint == incident.Fingerprint, cancellationToken);
        if (entity is null)
        {
            entity = ToEntity(incident);
            await context.Incidents.AddAsync(entity, cancellationToken);
        }
        else
        {
            if (entity.ResolvedAt is not null)
                await RemoveTodoStateAsync(entity, cancellationToken);
            ApplyReport(entity, incident);
        }

        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (
            ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // Another worker/process may have inserted the same fingerprint after
            // our lookup. The unique index is the authority; detach the losing row
            // and merge into the winner after EF's transaction has rolled back.
            context.Entry(entity).State = EntityState.Detached;
            entity = await context.Incidents
                .FirstAsync(candidate => candidate.Fingerprint == incident.Fingerprint, cancellationToken);
            if (entity.ResolvedAt is not null)
                await RemoveTodoStateAsync(entity, cancellationToken);
            ApplyReport(entity, incident);
            await context.SaveChangesAsync(cancellationToken);
        }
        return ToRecord(entity);
    }

    public async Task<Incident?> ResolveByFingerprintAsync(
        string fingerprint,
        DateTimeOffset resolvedAt,
        CancellationToken cancellationToken)
    {
        var entity = await context.Incidents
            .FirstOrDefaultAsync(candidate => candidate.Fingerprint == fingerprint, cancellationToken);
        if (entity is null) return null;

        if (entity.ResolvedAt is null)
        {
            await RemoveTodoStateAsync(entity, cancellationToken);
            entity.ResolvedAt = resolvedAt;
            entity.UpdatedAt = resolvedAt;
            entity.LastRetryError = null;
            await context.SaveChangesAsync(cancellationToken);
        }

        return ToRecord(entity);
    }

    private async Task RemoveTodoStateAsync(
        IncidentEntity incident,
        CancellationToken cancellationToken)
    {
        var key = incident.Occurrence <= 1
            ? "incident:" + incident.Id
            : $"incident:{incident.Id}:{incident.Occurrence}";
        var state = await context.TodoItemStates.FindAsync([key], cancellationToken);
        if (state is not null)
            context.TodoItemStates.Remove(state);
    }

    public async Task<Incident?> RecordRetryAsync(
        Guid id,
        DateTimeOffset retriedAt,
        string? error,
        bool resolve,
        CancellationToken cancellationToken)
    {
        var occurrence = resolve
            ? await context.Incidents
                .AsNoTracking()
                .Where(incident => incident.Id == id)
                .Select(incident => (int?)incident.Occurrence)
                .SingleOrDefaultAsync(cancellationToken)
            : null;
        var affected = resolve
            ? await context.Incidents
                .Where(incident => incident.Id == id)
                .ExecuteUpdateAsync(setters => setters
                        .SetProperty(incident => incident.RetryCount, incident => incident.RetryCount + 1)
                        .SetProperty(incident => incident.LastRetryAt, retriedAt)
                        .SetProperty(incident => incident.LastRetryError, error)
                        .SetProperty(incident => incident.UpdatedAt, retriedAt)
                        .SetProperty(incident => incident.ResolvedAt, retriedAt),
                    cancellationToken)
            : await context.Incidents
                .Where(incident => incident.Id == id)
                .ExecuteUpdateAsync(setters => setters
                        .SetProperty(incident => incident.RetryCount, incident => incident.RetryCount + 1)
                        .SetProperty(incident => incident.LastRetryAt, retriedAt)
                        .SetProperty(incident => incident.LastRetryError, error)
                        .SetProperty(incident => incident.UpdatedAt, retriedAt),
                    cancellationToken);
        if (affected == 0) return null;
        if (resolve && occurrence.HasValue)
        {
            var key = occurrence.Value <= 1
                ? "incident:" + id
                : $"incident:{id}:{occurrence.Value}";
            await context.TodoItemStates
                .Where(state => state.Key == key)
                .ExecuteDeleteAsync(cancellationToken);
        }
        var entity = await context.Incidents
            .AsNoTracking()
            .FirstAsync(incident => incident.Id == id, cancellationToken);
        return ToRecord(entity);
    }

    private static Incident ToRecord(IncidentEntity entity) => new(
        entity.Id,
        entity.Fingerprint,
        entity.Type,
        entity.Severity,
        entity.Title,
        entity.Detail,
        entity.SourceId,
        entity.DetectedAt,
        entity.UpdatedAt,
        entity.ResolvedAt,
        entity.RetryCount,
        entity.LastRetryAt,
        entity.LastRetryError,
        entity.Occurrence);

    private static IncidentEntity ToEntity(Incident record) => new()
    {
        Id = record.Id,
        Fingerprint = record.Fingerprint,
        Type = record.Type,
        Severity = record.Severity,
        Title = record.Title,
        Detail = record.Detail,
        SourceId = record.SourceId,
        DetectedAt = record.DetectedAt,
        UpdatedAt = record.UpdatedAt,
        ResolvedAt = record.ResolvedAt,
        RetryCount = record.RetryCount,
        LastRetryAt = record.LastRetryAt,
        LastRetryError = record.LastRetryError,
        Occurrence = Math.Max(1, record.Occurrence)
    };

    private static void ApplyReport(IncidentEntity entity, Incident incident)
    {
        var isReopening = entity.ResolvedAt is not null;
        entity.Type = incident.Type;
        entity.Severity = incident.Severity;
        entity.Title = incident.Title;
        entity.Detail = incident.Detail;
        entity.SourceId = incident.SourceId;
        entity.UpdatedAt = incident.UpdatedAt;
        // Keep the logical incident and its first-seen/retry history, but give
        // each resolved -> open transition a stable occurrence discriminator.
        // Concurrent reporters calculate the same next number, so the outbox's
        // unique key still coalesces duplicate reports for that occurrence.
        if (isReopening)
            entity.Occurrence = Math.Max(1, entity.Occurrence) + 1;
        entity.ResolvedAt = null;
    }
}
