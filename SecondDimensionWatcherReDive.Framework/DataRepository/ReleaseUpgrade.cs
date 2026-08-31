namespace SecondDimensionWatcherReDive.Framework.DataRepository;

public enum ReleaseUpgradeStatus
{
    Downloading,
    Verifying,
    Applied,
    Failed,
    RolledBack,
    Completed
}

public enum ReleaseUpgradeMappingKind
{
    Previous,
    Candidate
}

public enum ReleaseUpgradeDownloadSubmissionMarkOutcome
{
    Submitted,
    AlreadySubmitted,
    Settled,
    LeaseLost,
    StateChanged,
    NotFound
}

public sealed record ReleaseUpgradeDownloadSubmissionLease(
    Guid Id,
    DateTimeOffset ExpiresAt,
    bool IsPrepared);

public sealed record ReleaseUpgradePendingDownloadCancellation(
    Guid OperationId,
    Guid CandidateReleaseId,
    Guid SubmissionLeaseId,
    bool RemoveFile);

public sealed record ReleaseUpgradeOperation(
    Guid Id,
    Guid CurrentReleaseId,
    Guid CandidateReleaseId,
    ReleaseUpgradeStatus Status,
    int CurrentScore,
    int CandidateScore,
    DateTimeOffset CreatedAt,
    DateTimeOffset? VerifiedAt,
    DateTimeOffset? AppliedAt,
    DateTimeOffset? RollbackUntil,
    DateTimeOffset? CompletedAt,
    string? FailureSummary,
    DateTimeOffset? DownloadPreparedAt = null,
    DateTimeOffset? DownloadSubmittedAt = null,
    Guid? DownloadSubmissionLeaseId = null,
    DateTimeOffset? DownloadSubmissionLeaseUntil = null,
    bool DownloadCancellationRemoveFile = false);

public sealed record ReleaseUpgradeMappingSnapshot(
    Guid Id,
    Guid OperationId,
    ReleaseUpgradeMappingKind Kind,
    Guid OriginalMappingId,
    Guid AnimationInfoId,
    string VirtualPath,
    string PhysicalPath,
    string FileStore);

public sealed record ReleaseUpgradeActivation(
    ReleaseUpgradeOperation Operation,
    IReadOnlyList<FileMapping> PreviousMappings,
    IReadOnlyList<FileMapping> CandidateMappings);

public sealed record ReleaseUpgradeMutationResult(
    bool IsSuccess,
    string Outcome,
    ReleaseUpgradeOperation? Operation);

public interface IReleaseUpgradeRepository
{
    Task<IReadOnlyList<ReleaseUpgradeCandidate>> GetCandidatesAsync(
        bool automaticOnly,
        int take,
        CancellationToken cancellationToken);

    Task<ReleaseUpgradeCandidate?> FindCandidateAsync(
        Guid currentReleaseId,
        Guid candidateReleaseId,
        CancellationToken cancellationToken);

    Task<ReleaseUpgradeOperation?> TryBeginAsync(
        ReleaseUpgradeCandidate candidate,
        DateTimeOffset createdAt,
        CancellationToken cancellationToken);

    Task<ReleaseUpgradeOperation?> FindActiveByCandidateAsync(
        Guid candidateReleaseId,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<Guid>> GetReadyCandidateIdsAsync(
        int take,
        CancellationToken cancellationToken);

    Task<ReleaseUpgradeDownloadSubmissionLease?> TryClaimDownloadSubmissionAsync(
        Guid operationId,
        Guid leaseId,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken);

    Task<ReleaseUpgradeDownloadSubmissionMarkOutcome> TryMarkDownloadSubmittedAsync(
        Guid operationId,
        Guid leaseId,
        Guid? downloadAttemptId,
        DateTimeOffset submittedAt,
        CancellationToken cancellationToken);

    Task<bool> TryMarkDownloadPreparedAsync(
        Guid operationId,
        Guid leaseId,
        DateTimeOffset preparedAt,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<ReleaseUpgradePendingDownloadCancellation>>
        GetPendingDownloadCancellationsAsync(
            int take,
            CancellationToken cancellationToken);

    Task<int> ClearExpiredDownloadSubmissionLeasesWithoutCancellationAsync(
        CancellationToken cancellationToken);

    Task<bool> TryDeferDownloadCancellationAsync(
        Guid operationId,
        Guid submissionLeaseId,
        TimeSpan retryDelay,
        CancellationToken cancellationToken);

    Task<bool> TryMarkDownloadCancellationReconciledAsync(
        Guid operationId,
        Guid submissionLeaseId,
        CancellationToken cancellationToken);

    Task<ReleaseUpgradeActivation?> GetActivationAsync(
        Guid candidateReleaseId,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<FileMapping>> GetCandidateMappingsAsync(
        Guid candidateReleaseId,
        CancellationToken cancellationToken);

    Task<ReleaseUpgradeActivation?> GetRollbackAsync(
        Guid operationId,
        CancellationToken cancellationToken);

    Task<ReleaseUpgradeMutationResult> ActivateAsync(
        Guid operationId,
        IReadOnlyList<FileMapping> expectedPreviousMappings,
        IReadOnlyList<FileMapping> expectedCandidateMappings,
        DateTimeOffset verifiedAt,
        DateTimeOffset rollbackUntil,
        CancellationToken cancellationToken);

    Task<ReleaseUpgradeMutationResult> MarkFailedAsync(
        Guid operationId,
        string failureSummary,
        CancellationToken cancellationToken);

    Task<ReleaseUpgradeMutationResult> RollbackAsync(
        Guid operationId,
        DateTimeOffset rolledBackAt,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<ReleaseUpgradeOperation>> GetHistoryAsync(
        int take,
        CancellationToken cancellationToken);

    Task<int> CompleteExpiredAsync(
        DateTimeOffset completedAt,
        CancellationToken cancellationToken);
}
