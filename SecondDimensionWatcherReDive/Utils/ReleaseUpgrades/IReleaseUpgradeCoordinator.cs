using SecondDimensionWatcherReDive.Framework.DataRepository;

namespace SecondDimensionWatcherReDive.Utils.ReleaseUpgrades;

public sealed record ReleaseUpgradeExecutionResult(
    bool IsSuccess,
    string Outcome,
    bool DryRun,
    bool RequiresDownload,
    ReleaseUpgradeOperation? Operation,
    IReadOnlyList<string> ValidationErrors);

public interface IReleaseUpgradeCoordinator
{
    Task<ReleaseUpgradeExecutionResult> ExecuteAsync(
        ReleaseUpgradeCandidate candidate,
        bool dryRun,
        CancellationToken cancellationToken);

    Task<ReleaseUpgradeExecutionResult?> TryActivateCandidateAsync(
        Guid candidateReleaseId,
        CancellationToken cancellationToken);

    Task ReconcilePendingDownloadCancellationAsync(
        ReleaseUpgradePendingDownloadCancellation pendingCancellation,
        CancellationToken cancellationToken);

    Task ReconcilePendingDownloadCancellationAsync(
        PendingDownloadCancellation pendingCancellation,
        CancellationToken cancellationToken);

    Task ReconcilePendingDownloadSubmissionAsync(
        PendingDownloadSubmission pendingSubmission,
        CancellationToken cancellationToken);

    Task<ReleaseUpgradeMutationResult> RollbackAsync(
        Guid operationId,
        CancellationToken cancellationToken);
}
