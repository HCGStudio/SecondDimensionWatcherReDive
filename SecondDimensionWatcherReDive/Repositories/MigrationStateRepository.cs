using Microsoft.EntityFrameworkCore;
using SecondDimensionWatcherReDive.Framework.DataRepository;
using MigrationStateEntity = SecondDimensionWatcherReDive.Models.MigrationExecutionState;

namespace SecondDimensionWatcherReDive.Repositories;

public sealed class MigrationStateRepository(Models.ApplicationContext context)
    : IMigrationStateRepository
{
    private DbSet<MigrationStateEntity> States => context.MigrationStates;

    public async Task<MigrationExecution?> FindAsync(
        string key,
        int version,
        CancellationToken cancellationToken)
    {
        var entity = await States
            .AsNoTracking()
            .SingleOrDefaultAsync(
                state => state.Key == key && state.Version == version,
                cancellationToken);
        return entity is null ? null : ToRecord(entity);
    }

    public async Task<IReadOnlyList<MigrationExecution>> GetAllAsync(
        CancellationToken cancellationToken)
    {
        var entities = await States
            .AsNoTracking()
            .OrderBy(state => state.Key)
            .ThenByDescending(state => state.Version)
            .ToListAsync(cancellationToken);
        return entities.Select(ToRecord).ToList();
    }

    public async Task<MigrationExecution> EnsurePendingAsync(
        string key,
        int version,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        ValidateIdentity(key, version);
        var entity = await States.SingleOrDefaultAsync(
            state => state.Key == key && state.Version == version,
            cancellationToken);
        if (entity is not null) return ToRecord(entity);

        entity = new MigrationStateEntity
        {
            Key = key,
            Version = version,
            Status = MigrationExecutionStatus.Pending,
            UpdatedAt = now,
            AttemptCount = 0
        };
        await States.AddAsync(entity, cancellationToken);
        await context.SaveChangesAsync(cancellationToken);
        return ToRecord(entity);
    }

    public async Task<MigrationExecution> MarkRunningAsync(
        string key,
        int version,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var entity = await GetRequiredAsync(key, version, cancellationToken);
        if (entity.Status == MigrationExecutionStatus.Completed)
            throw InvalidTransition(entity, MigrationExecutionStatus.Running);

        entity.Status = MigrationExecutionStatus.Running;
        entity.StartedAt = now;
        entity.FinishedAt = null;
        entity.UpdatedAt = now;
        entity.AttemptCount = checked(entity.AttemptCount + 1);
        await context.SaveChangesAsync(cancellationToken);
        return ToRecord(entity);
    }

    public async Task<MigrationExecution> SaveCheckpointAsync(
        string key,
        int version,
        string? checkpoint,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        ValidateCheckpoint(checkpoint);
        var entity = await GetRequiredAsync(key, version, cancellationToken);
        if (entity.Status != MigrationExecutionStatus.Running)
            throw InvalidTransition(entity, MigrationExecutionStatus.Running);

        entity.Checkpoint = checkpoint;
        entity.UpdatedAt = now;
        await context.SaveChangesAsync(cancellationToken);
        return ToRecord(entity);
    }

    public async Task<MigrationExecution> MarkCompletedAsync(
        string key,
        int version,
        string? checkpoint,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        ValidateCheckpoint(checkpoint);
        var entity = await GetRequiredAsync(key, version, cancellationToken);
        if (entity.Status != MigrationExecutionStatus.Running)
            throw InvalidTransition(entity, MigrationExecutionStatus.Completed);

        entity.Status = MigrationExecutionStatus.Completed;
        entity.Checkpoint = checkpoint;
        entity.FinishedAt = now;
        entity.UpdatedAt = now;
        await context.SaveChangesAsync(cancellationToken);
        return ToRecord(entity);
    }

    public async Task<MigrationExecution> MarkFailedAsync(
        string key,
        int version,
        string? checkpoint,
        string errorSummary,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        ValidateCheckpoint(checkpoint);
        ArgumentException.ThrowIfNullOrWhiteSpace(errorSummary);
        if (errorSummary.Length > 4096)
            throw new ArgumentOutOfRangeException(nameof(errorSummary));

        var entity = await GetRequiredAsync(key, version, cancellationToken);
        if (entity.Status == MigrationExecutionStatus.Completed)
            throw InvalidTransition(entity, MigrationExecutionStatus.Failed);

        entity.Status = MigrationExecutionStatus.Failed;
        entity.Checkpoint = checkpoint;
        entity.LastErrorSummary = errorSummary;
        entity.FinishedAt = now;
        entity.UpdatedAt = now;
        await context.SaveChangesAsync(cancellationToken);
        return ToRecord(entity);
    }

    private async Task<MigrationStateEntity> GetRequiredAsync(
        string key,
        int version,
        CancellationToken cancellationToken)
    {
        ValidateIdentity(key, version);
        return await States.SingleOrDefaultAsync(
                   state => state.Key == key && state.Version == version,
                   cancellationToken)
               ?? throw new InvalidOperationException(
                   $"Migration state '{key}' version {version} does not exist.");
    }

    private static MigrationExecution ToRecord(MigrationStateEntity entity) => new(
        entity.Key,
        entity.Version,
        entity.Status,
        entity.Checkpoint,
        entity.StartedAt,
        entity.FinishedAt,
        entity.UpdatedAt,
        entity.AttemptCount,
        entity.LastErrorSummary);

    private static void ValidateIdentity(string key, int version)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        if (key.Length > 256) throw new ArgumentOutOfRangeException(nameof(key));
        if (version <= 0) throw new ArgumentOutOfRangeException(nameof(version));
    }

    private static void ValidateCheckpoint(string? checkpoint)
    {
        if (checkpoint?.Length > 4096)
            throw new ArgumentOutOfRangeException(nameof(checkpoint));
    }

    private static InvalidOperationException InvalidTransition(
        MigrationStateEntity entity,
        MigrationExecutionStatus requested) => new(
        $"Cannot transition migration '{entity.Key}' version {entity.Version} " +
        $"from {entity.Status} to {requested}.");
}
