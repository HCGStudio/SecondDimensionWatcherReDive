using Microsoft.EntityFrameworkCore;
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
        // A competing lease is an expected outcome, not an insert failure.
        // Decide creation or takeover atomically, including concurrent first runs.
        var affected = await context.Database.CreateExecutionStrategy().ExecuteAsync(
            token => context.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO "ScheduledTaskStates" AS state
                    ("TaskId", "LeaseOwner", "LeaseExpiresAt", "LastStartedAt", "RunCount")
                VALUES ({taskId}, {ownerId}, {leaseUntil}, {now}, 1)
                ON CONFLICT ("TaskId") DO UPDATE
                SET "LeaseOwner" = EXCLUDED."LeaseOwner",
                    "LeaseExpiresAt" = EXCLUDED."LeaseExpiresAt",
                    "LastStartedAt" = EXCLUDED."LastStartedAt",
                    "RunCount" = state."RunCount" + 1
                WHERE state."LeaseOwner" IS NULL
                   OR state."LeaseExpiresAt" <= EXCLUDED."LastStartedAt"
                   OR state."LeaseOwner" = EXCLUDED."LeaseOwner"
                   OR ({force}
                       AND state."LastCompletedAt" IS NOT NULL
                       AND (state."LastStartedAt" IS NULL
                            OR state."LastCompletedAt" >= state."LastStartedAt"))
                """, token), cancellationToken);
        return affected == 1;
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
