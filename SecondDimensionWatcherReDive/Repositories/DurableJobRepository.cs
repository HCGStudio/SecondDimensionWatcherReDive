using Microsoft.EntityFrameworkCore;
using SecondDimensionWatcherReDive.Framework.DataRepository;
using JobEntity = SecondDimensionWatcherReDive.Models.DurableJob;

namespace SecondDimensionWatcherReDive.Repositories;

public sealed class DurableJobRepository(Models.ApplicationContext context)
    : IDurableJobRepository
{
    public async Task<IReadOnlyList<DurableJob>> ClaimDueAsync(
        string workerId,
        DateTimeOffset now,
        DateTimeOffset leaseUntil,
        int take,
        CancellationToken cancellationToken)
    {
        var candidateIds = await context.DurableJobs
            .AsNoTracking()
            .Where(job =>
                job.NextAttemptAt <= now
                && (job.Status == DurableJobStatus.Pending
                    || (job.Status == DurableJobStatus.Processing
                        && job.LeaseExpiresAt <= now)))
            .OrderBy(job => job.NextAttemptAt)
            .ThenBy(job => job.CreatedAt)
            .Select(job => job.Id)
            .Take(take)
            .ToListAsync(cancellationToken);

        var claimedIds = new List<Guid>(candidateIds.Count);
        foreach (var id in candidateIds)
        {
            var affected = await context.DurableJobs
                .Where(job => job.Id == id
                              && job.NextAttemptAt <= now
                              && (job.Status == DurableJobStatus.Pending
                                  || (job.Status == DurableJobStatus.Processing
                                      && job.LeaseExpiresAt <= now)))
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(job => job.Status, DurableJobStatus.Processing)
                    .SetProperty(job => job.LeaseOwner, workerId)
                    .SetProperty(job => job.LeaseExpiresAt, leaseUntil)
                    .SetProperty(job => job.UpdatedAt, now), cancellationToken);
            if (affected == 1)
                claimedIds.Add(id);
        }

        if (claimedIds.Count == 0)
            return [];

        return (await context.DurableJobs
                .AsNoTracking()
                .Where(job => claimedIds.Contains(job.Id))
                .OrderBy(job => job.CreatedAt)
                .ToListAsync(cancellationToken))
            .Select(ToRecord)
            .ToList();
    }

    public async Task<bool> AdvanceStageAsync(
        Guid id,
        string workerId,
        DurableJobStage expectedStage,
        DurableJobStage nextStage,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var completed = nextStage == DurableJobStage.Done;
        var affected = await context.DurableJobs
            .Where(job => job.Id == id
                          && job.Status == DurableJobStatus.Processing
                          && job.LeaseOwner == workerId
                          && job.LeaseExpiresAt > now
                          && job.Stage == expectedStage)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(job => job.Stage, nextStage)
                .SetProperty(job => job.Status,
                    completed ? DurableJobStatus.Completed : DurableJobStatus.Processing)
                .SetProperty(job => job.UpdatedAt, now)
                .SetProperty(job => job.LastAttemptAt, now)
                .SetProperty(job => job.CompletedAt, completed ? now : null)
                .SetProperty(job => job.LeaseOwner, completed ? null : workerId)
                .SetProperty(job => job.LeaseExpiresAt,
                    job => completed ? null : job.LeaseExpiresAt)
                .SetProperty(job => job.LastError, (string?)null), cancellationToken);
        return affected == 1;
    }

    public async Task<bool> RenewLeaseAsync(
        Guid id,
        string workerId,
        DateTimeOffset now,
        DateTimeOffset leaseUntil,
        CancellationToken cancellationToken)
    {
        var affected = await context.DurableJobs
            .Where(job => job.Id == id
                          && job.Status == DurableJobStatus.Processing
                          && job.LeaseOwner == workerId
                          && job.LeaseExpiresAt > now)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(job => job.LeaseExpiresAt, leaseUntil)
                .SetProperty(job => job.UpdatedAt, now), cancellationToken);
        return affected == 1;
    }

    public Task MarkFailedAsync(
        Guid id,
        string workerId,
        int attemptCount,
        DateTimeOffset attemptedAt,
        DateTimeOffset? nextAttemptAt,
        string error,
        CancellationToken cancellationToken) =>
        context.DurableJobs
            .Where(job => job.Id == id
                          && job.Status == DurableJobStatus.Processing
                          && job.LeaseOwner == workerId)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(job => job.Status,
                    nextAttemptAt.HasValue
                        ? DurableJobStatus.Pending
                        : DurableJobStatus.DeadLetter)
                .SetProperty(job => job.AttemptCount, attemptCount)
                .SetProperty(job => job.LastAttemptAt, attemptedAt)
                .SetProperty(job => job.UpdatedAt, attemptedAt)
                .SetProperty(job => job.NextAttemptAt, nextAttemptAt ?? attemptedAt)
                .SetProperty(job => job.LeaseOwner, (string?)null)
                .SetProperty(job => job.LeaseExpiresAt, (DateTimeOffset?)null)
                .SetProperty(job => job.LastError, error), cancellationToken);

    public async Task<DurableJobPage> GetPageAsync(
        DurableJobStatus? status,
        int skip,
        int take,
        CancellationToken cancellationToken)
    {
        var query = context.DurableJobs.AsNoTracking().AsQueryable();
        if (status.HasValue)
            query = query.Where(job => job.Status == status.Value);

        var totalCount = await query.CountAsync(cancellationToken);
        var jobs = await query
            .OrderByDescending(job => job.UpdatedAt)
            .ThenByDescending(job => job.CreatedAt)
            .Skip(skip)
            .Take(take)
            .ToListAsync(cancellationToken);
        return new DurableJobPage(jobs.Select(ToRecord).ToList(), totalCount);
    }

    public Task<int> RetryAsync(
        IReadOnlyCollection<Guid> ids,
        DateTimeOffset now,
        CancellationToken cancellationToken) =>
        context.DurableJobs
            .Where(job => ids.Contains(job.Id)
                          && job.Status == DurableJobStatus.DeadLetter)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(job => job.Status, DurableJobStatus.Pending)
                .SetProperty(job => job.AttemptCount, 0)
                .SetProperty(job => job.NextAttemptAt, now)
                .SetProperty(job => job.UpdatedAt, now)
                .SetProperty(job => job.LastError, (string?)null)
                .SetProperty(job => job.LeaseOwner, (string?)null)
                .SetProperty(job => job.LeaseExpiresAt, (DateTimeOffset?)null), cancellationToken);

    public Task<int> ResolveAsync(
        IReadOnlyCollection<Guid> ids,
        DateTimeOffset now,
        CancellationToken cancellationToken) =>
        context.DurableJobs
            .Where(job => ids.Contains(job.Id)
                          && job.Status == DurableJobStatus.DeadLetter)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(job => job.Status, DurableJobStatus.Resolved)
                .SetProperty(job => job.CompletedAt, now)
                .SetProperty(job => job.UpdatedAt, now)
                .SetProperty(job => job.LeaseOwner, (string?)null)
                .SetProperty(job => job.LeaseExpiresAt, (DateTimeOffset?)null), cancellationToken);

    public async Task<DurableJobStatistics> GetStatisticsAsync(
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var pendingCount = await context.DurableJobs.CountAsync(
            job => job.Status == DurableJobStatus.Pending, cancellationToken);
        var processingCount = await context.DurableJobs.CountAsync(
            job => job.Status == DurableJobStatus.Processing, cancellationToken);
        var deadLetterCount = await context.DurableJobs.CountAsync(
            job => job.Status == DurableJobStatus.DeadLetter, cancellationToken);
        var oldest = await context.DurableJobs
            .Where(job => job.Status == DurableJobStatus.Pending)
            .MinAsync(job => (DateTimeOffset?)job.CreatedAt, cancellationToken);
        return new DurableJobStatistics(
            pendingCount,
            processingCount,
            deadLetterCount,
            oldest.HasValue ? Math.Max(0, (now - oldest.Value).TotalSeconds) : 0);
    }

    private static DurableJob ToRecord(JobEntity job) => new(
        job.Id,
        job.DeduplicationKey,
        job.Type,
        job.Status,
        job.Stage,
        job.PayloadJson,
        job.AttemptCount,
        job.CreatedAt,
        job.UpdatedAt,
        job.NextAttemptAt,
        job.LastAttemptAt,
        job.CompletedAt,
        job.LeaseOwner,
        job.LeaseExpiresAt,
        job.LastError);
}
