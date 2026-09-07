using Microsoft.EntityFrameworkCore;
using Npgsql;
using SecondDimensionWatcherReDive.Framework.DataRepository;

namespace SecondDimensionWatcherReDive.Repositories;

public sealed class ScheduledTaskLeaseRepository(Models.ApplicationContext context)
    : IScheduledTaskLeaseRepository
{
    public async Task<bool> TryAcquireAsync(
        string taskId,
        string ownerId,
        DateTimeOffset now,
        DateTimeOffset leaseUntil,
        bool force,
        CancellationToken cancellationToken)
    {
        var affected = await context.ScheduledTaskStates
            .Where(state => state.TaskId == taskId
                            && (state.LeaseOwner == null
                                || state.LeaseExpiresAt <= now
                                || state.LeaseOwner == ownerId
                                || (force
                                    && state.LastCompletedAt != null
                                    && (state.LastStartedAt == null
                                        || state.LastCompletedAt >= state.LastStartedAt))))
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(state => state.LeaseOwner, ownerId)
                .SetProperty(state => state.LeaseExpiresAt, leaseUntil)
                .SetProperty(state => state.LastStartedAt, now)
                .SetProperty(state => state.RunCount, state => state.RunCount + 1),
                cancellationToken);
        if (affected == 1)
            return true;

        var state = new Models.ScheduledTaskState
        {
            TaskId = taskId,
            LeaseOwner = ownerId,
            LeaseExpiresAt = leaseUntil,
            LastStartedAt = now,
            RunCount = 1
        };
        await context.ScheduledTaskStates.AddAsync(state, cancellationToken);
        try
        {
            await context.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException exception) when (
            exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            context.ChangeTracker.Clear();
            return false;
        }
    }

    public async Task<bool> RenewAsync(
        string taskId,
        string ownerId,
        DateTimeOffset now,
        DateTimeOffset leaseUntil,
        CancellationToken cancellationToken)
    {
        var affected = await context.ScheduledTaskStates
            .Where(state => state.TaskId == taskId
                            && state.LeaseOwner == ownerId)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(state => state.LeaseExpiresAt, leaseUntil), cancellationToken);
        return affected == 1;
    }

    public Task CompleteAsync(
        string taskId,
        string ownerId,
        DateTimeOffset completedAt,
        DateTimeOffset leaseUntil,
        bool succeeded,
        string? error,
        CancellationToken cancellationToken) =>
        context.ScheduledTaskStates
            .Where(state => state.TaskId == taskId
                            && state.LeaseOwner == ownerId)
            .ExecuteUpdateAsync(setters => setters
                // Keep a cooldown lease until the next periodic due time. Other
                // instances poll this row and can take over promptly after a crash
                // without immediately duplicating a normally completed run.
                .SetProperty(state => state.LeaseOwner, ownerId)
                .SetProperty(state => state.LeaseExpiresAt, leaseUntil)
                .SetProperty(state => state.LastCompletedAt, completedAt)
                .SetProperty(state => state.LastSucceededAt,
                    state => succeeded ? completedAt : state.LastSucceededAt)
                .SetProperty(state => state.LastError,
                    succeeded ? null : error), cancellationToken);

    public async Task<IReadOnlyList<ScheduledTaskLeaseState>> GetStatesAsync(
        IReadOnlyCollection<string> taskIds,
        CancellationToken cancellationToken)
    {
        if (taskIds.Count == 0) return [];

        var ids = taskIds.Distinct(StringComparer.Ordinal).ToArray();
        return await context.ScheduledTaskStates
            .AsNoTracking()
            .Where(state => ids.Contains(state.TaskId))
            .Select(state => new ScheduledTaskLeaseState(
                state.TaskId,
                state.LeaseOwner,
                state.LeaseExpiresAt,
                state.LastStartedAt,
                state.LastCompletedAt))
            .ToListAsync(cancellationToken);
    }
}
