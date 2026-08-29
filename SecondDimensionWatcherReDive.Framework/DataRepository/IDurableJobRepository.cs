namespace SecondDimensionWatcherReDive.Framework.DataRepository;

public enum DurableJobType
{
    DownloadCompletion
}

public enum DurableJobStatus
{
    Pending,
    Processing,
    Completed,
    DeadLetter,
    Resolved
}

public enum DurableJobStage
{
    MapFiles,
    Notify,
    InvokePlugins,
    Done
}

public sealed record DurableJob(
    Guid Id,
    string DeduplicationKey,
    DurableJobType Type,
    DurableJobStatus Status,
    DurableJobStage Stage,
    string PayloadJson,
    int AttemptCount,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset NextAttemptAt,
    DateTimeOffset? LastAttemptAt,
    DateTimeOffset? CompletedAt,
    string? LeaseOwner,
    DateTimeOffset? LeaseExpiresAt,
    string? LastError);

public sealed record DurableJobPage(
    IReadOnlyList<DurableJob> Items,
    int TotalCount);

public sealed record DurableJobStatistics(
    int PendingCount,
    int ProcessingCount,
    int DeadLetterCount,
    double OldestPendingAgeSeconds);

public sealed record DownloadCompletionJobPayload(
    Guid ItemId,
    string StorePath,
    string FileStore,
    Guid? DownloadAttemptId);

public interface IDurableJobRepository
{
    Task<IReadOnlyList<DurableJob>> ClaimDueAsync(
        string workerId,
        DateTimeOffset now,
        DateTimeOffset leaseUntil,
        int take,
        CancellationToken cancellationToken);

    Task<bool> AdvanceStageAsync(
        Guid id,
        string workerId,
        DurableJobStage expectedStage,
        DurableJobStage nextStage,
        DateTimeOffset now,
        CancellationToken cancellationToken);

    Task<bool> RenewLeaseAsync(
        Guid id,
        string workerId,
        DateTimeOffset now,
        DateTimeOffset leaseUntil,
        CancellationToken cancellationToken);

    Task MarkFailedAsync(
        Guid id,
        string workerId,
        int attemptCount,
        DateTimeOffset attemptedAt,
        DateTimeOffset? nextAttemptAt,
        string error,
        CancellationToken cancellationToken);

    Task<DurableJobPage> GetPageAsync(
        DurableJobStatus? status,
        int skip,
        int take,
        CancellationToken cancellationToken);

    Task<int> RetryAsync(
        IReadOnlyCollection<Guid> ids,
        DateTimeOffset now,
        CancellationToken cancellationToken);

    Task<int> ResolveAsync(
        IReadOnlyCollection<Guid> ids,
        DateTimeOffset now,
        CancellationToken cancellationToken);

    Task<DurableJobStatistics> GetStatisticsAsync(
        DateTimeOffset now,
        CancellationToken cancellationToken);
}
