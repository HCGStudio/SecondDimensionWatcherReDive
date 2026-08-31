using System.Diagnostics;
using SecondDimensionWatcherReDive.Framework.DataRepository;
using SecondDimensionWatcherReDive.Framework.FileDownload;
using SecondDimensionWatcherReDive.Framework.FileStore;
using SecondDimensionWatcherReDive.Framework.PluginParams;
using SecondDimensionWatcherReDive.Plugin;
using SecondDimensionWatcherReDive.Utils.FileStore;
using SecondDimensionWatcherReDive.Utils.Incidents;

namespace SecondDimensionWatcherReDive.Utils.ReleaseUpgrades;

public sealed class ReleaseUpgradeCoordinator(
    IReleaseUpgradeRepository upgradeRepository,
    IAnimationInfoRepository animationInfoRepository,
    ISubscriptionAutomationPolicyRepository policyRepository,
    IFileMappingRepository fileMappingRepository,
    IFileDownloadClientProvider downloadClientProvider,
    IFileStoreProvider fileStoreProvider,
    IPluginEventTrigger<FileDownloadStartParam> beforeDownloadStartEventTrigger,
    IIncidentReporter incidentReporter,
    ILogger<ReleaseUpgradeCoordinator> logger) : IReleaseUpgradeCoordinator
{
    private static readonly TimeSpan DownloadSubmissionLeaseDuration = TimeSpan.FromMinutes(3);
    private static readonly TimeSpan DownloadSubmissionRemoteBudget = TimeSpan.FromSeconds(90);
    private static readonly TimeSpan DownloadCancellationLeaseDuration = TimeSpan.FromMinutes(3);
    private static readonly TimeSpan DownloadCancellationRemoteBudget = TimeSpan.FromSeconds(90);
    private static readonly TimeSpan DownloadCancellationRetryDelay = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan DownloadLeaseSafetyMargin = TimeSpan.FromSeconds(1);

    public async Task<ReleaseUpgradeExecutionResult> ExecuteAsync(
        ReleaseUpgradeCandidate candidate,
        bool dryRun,
        CancellationToken cancellationToken)
    {
        var next = await animationInfoRepository.FindByIdAsync(
            candidate.CandidateReleaseId,
            cancellationToken);
        if (next is null)
            return Result(false, "candidate_not_found", dryRun, false, null, ["Candidate release does not exist."]);

        var requiresDownload = !next.IsDownloadFinished;
        if (dryRun)
        {
            var validationErrors = requiresDownload
                ? Array.Empty<string>()
                : await ValidateCandidateAsync(candidate, cancellationToken);
            return Result(validationErrors.Count == 0,
                validationErrors.Count == 0 ? "ready" : "validation_failed",
                true,
                requiresDownload,
                null,
                validationErrors);
        }

        var operation = await upgradeRepository.TryBeginAsync(
            candidate,
            DateTimeOffset.UtcNow,
            cancellationToken);
        if (operation is null)
            return Result(false, "upgrade_already_started", false, requiresDownload, null,
                ["Another worker already claimed this upgrade."]);

        return await ContinueOperationAsync(operation, cancellationToken);
    }

    public async Task<ReleaseUpgradeExecutionResult?> TryActivateCandidateAsync(
        Guid candidateReleaseId,
        CancellationToken cancellationToken)
    {
        var operation = await upgradeRepository.FindActiveByCandidateAsync(
            candidateReleaseId,
            cancellationToken);
        return operation is null
            ? null
            : await ContinueOperationAsync(operation, cancellationToken);
    }

    public Task ReconcilePendingDownloadCancellationAsync(
        ReleaseUpgradePendingDownloadCancellation pendingCancellation,
        CancellationToken cancellationToken) =>
        ReconcilePendingDownloadCancellationCoreAsync(
            pendingCancellation.CandidateReleaseId,
            pendingCancellation.RemoveFile,
            pendingCancellation,
            cancellationToken);

    public Task ReconcilePendingDownloadCancellationAsync(
        PendingDownloadCancellation pendingCancellation,
        CancellationToken cancellationToken) =>
        ReconcilePendingDownloadCancellationCoreAsync(
            pendingCancellation.AnimationInfoId,
            pendingCancellation.RemoveFile,
            upgradeCancellation: null,
            cancellationToken);

    public async Task ReconcilePendingDownloadSubmissionAsync(
        PendingDownloadSubmission pendingSubmission,
        CancellationToken cancellationToken)
    {
        var info = await animationInfoRepository.FindByIdAsync(
            pendingSubmission.AnimationInfoId,
            cancellationToken);
        if (info is null ||
            !info.IsDownloadTracked ||
            info.IsDownloadFinished ||
            info.DownloadAttemptId != pendingSubmission.DownloadAttemptId ||
            info.DownloadCancellationId is not null)
            return;

        var cancellationAttemptId = Guid.NewGuid();
        var leaseRequestStartedAt = Stopwatch.GetTimestamp();
        var cancellationLease = await animationInfoRepository
            .TryBeginExpiredDownloadSubmissionRecoveryAsync(
            info.Id,
            pendingSubmission.DownloadAttemptId,
            pendingSubmission.SubmissionLeaseId,
            cancellationAttemptId,
            Guid.NewGuid(),
            DownloadCancellationLeaseDuration,
            info.AutomationDisposition == SubscriptionAutomationDisposition.AutoDownloadQueued
                ? SubscriptionAutomationDisposition.AutoDownloadFailed
                : null,
            cancellationToken);
        if (cancellationLease is null)
            return;

        await TryReconcileClaimedCancellationAsync(
            info,
            pendingSubmission.DownloadAttemptId,
            cancellationAttemptId,
            cancellationLease,
            leaseRequestStartedAt,
            cancellationToken);
    }

    private async Task ReconcilePendingDownloadCancellationCoreAsync(
        Guid animationInfoId,
        bool removeFile,
        ReleaseUpgradePendingDownloadCancellation? upgradeCancellation,
        CancellationToken cancellationToken)
    {
        var info = await animationInfoRepository.FindByIdAsync(
            animationInfoId,
            cancellationToken);
        if (info?.DownloadCancellationId is not { } cancellationAttemptId)
            return;

        Guid? downloadAttemptId = info.IsDownloadTracked
            ? info.DownloadAttemptId
            : Guid.NewGuid();
        var leaseRequestStartedAt = Stopwatch.GetTimestamp();
        var cancellationLease = await animationInfoRepository.TryBeginCancelDownloadAsync(
                info.Id,
                downloadAttemptId,
                cancellationAttemptId,
                Guid.NewGuid(),
                DownloadCancellationLeaseDuration,
                removeFile,
                requireUnfinished: false,
                terminalDisposition: null,
                cancellationToken);
        if (cancellationLease is null)
        {
            if (upgradeCancellation is not null)
                await DeferDownloadCancellationSafelyAsync(
                    upgradeCancellation,
                    cancellationToken);
            return;
        }

        if (!await TryReconcileClaimedCancellationAsync(
                info,
                downloadAttemptId,
                cancellationAttemptId,
                cancellationLease,
                leaseRequestStartedAt,
                cancellationToken))
            return;

        if (upgradeCancellation is not null)
            await upgradeRepository.TryMarkDownloadCancellationReconciledAsync(
                upgradeCancellation.OperationId,
                upgradeCancellation.SubmissionLeaseId,
                cancellationToken);
    }

    private async Task<bool> TryReconcileClaimedCancellationAsync(
        AnimationInfo info,
        Guid? downloadAttemptId,
        Guid cancellationAttemptId,
        DownloadCancellationLease cancellationLease,
        long leaseRequestStartedAt,
        CancellationToken cancellationToken)
    {
        var client = downloadClientProvider.GetRequiredClient(info.DownloadType);
        var remainingRemoteBudget = DownloadCancellationRemoteBudget -
                                    Stopwatch.GetElapsedTime(leaseRequestStartedAt);
        if (remainingRemoteBudget <= TimeSpan.Zero)
            return false;
        using var remoteCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken);
        remoteCancellation.CancelAfter(remainingRemoteBudget);
        remoteCancellation.Token.ThrowIfCancellationRequested();
        var cancellation = await client.CancelDownloadTaskAsync(
            info.Id,
            info.DownloadUrl,
            info.CachedDownloadData,
            info.AdditionalDownloadInfo,
            cancellationLease.RemoveFile,
            remoteCancellation.Token);
        if (!cancellation.IsSuccess)
        {
            await QueryDownloadProgressSafelyAsync(client, info, cancellationToken);
            return false;
        }

        using var finalizeCancellation = CreateLeaseBoundTokenSource(
            cancellationToken,
            leaseRequestStartedAt,
            DownloadCancellationLeaseDuration);
        if (finalizeCancellation is null)
            return false;
        if (!await fileMappingRepository.TryFinalizeDownloadCancellationAsync(
                info.Id,
                downloadAttemptId,
                cancellationAttemptId,
                cancellationLease.Id,
                terminalDisposition: null,
                finalizeCancellation.Token))
            return false;
        return true;
    }

    private async Task DeferDownloadCancellationSafelyAsync(
        ReleaseUpgradePendingDownloadCancellation pendingCancellation,
        CancellationToken cancellationToken)
    {
        try
        {
            await upgradeRepository.TryDeferDownloadCancellationAsync(
                pendingCancellation.OperationId,
                pendingCancellation.SubmissionLeaseId,
                DownloadCancellationRetryDelay,
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogWarning(
                exception,
                "Could not defer release-upgrade cancellation reconciliation for operation {OperationId}",
                pendingCancellation.OperationId);
        }
    }

    private async Task<ReleaseUpgradeExecutionResult> ContinueOperationAsync(
        ReleaseUpgradeOperation operation,
        CancellationToken cancellationToken)
    {
        if (operation.Status == ReleaseUpgradeStatus.Verifying)
            return await ActivateAsync(operation.CandidateReleaseId, cancellationToken);

        var next = await animationInfoRepository.FindByIdAsync(
            operation.CandidateReleaseId,
            cancellationToken);
        if (next is null)
            return await FailAsync(operation, "Candidate release disappeared after claim.", cancellationToken);
        if (next.IsDownloadFinished)
            return await ActivateAsync(operation.CandidateReleaseId, cancellationToken);
        if (next.IsDownloadTracked &&
            next.DownloadCancellationId is null &&
            operation.DownloadSubmittedAt is not null)
            return Result(true, "download_in_progress", false, true, operation, []);
        if (next.DownloadCancellationId is not null)
            return await FailAsync(operation, "Candidate download cancellation is in progress.", cancellationToken);

        var leaseId = Guid.NewGuid();
        var leaseRequestStartedAt = Stopwatch.GetTimestamp();
        var lease = await upgradeRepository.TryClaimDownloadSubmissionAsync(
                operation.Id,
                leaseId,
                DownloadSubmissionLeaseDuration,
                cancellationToken);
        if (lease is null)
            return Result(true, "download_submission_in_progress", false, true, operation, []);

        var downloadAttemptId = next.DownloadAttemptId ?? Guid.NewGuid();
        var recoveringPersistedSubmission = next.IsDownloadTracked;
        IFileDownloadClient? client = null;
        var preparationAttempted = false;
        var downloadStartAttempted = false;
        var submissionAttempted = false;
        var submissionUncertain = false;
        try
        {
            var remainingRemoteBudget = DownloadSubmissionRemoteBudget -
                                        Stopwatch.GetElapsedTime(leaseRequestStartedAt);
            if (remainingRemoteBudget <= TimeSpan.Zero)
                return await RecoveryPendingAsync(
                    operation,
                    "The durable submission lease was acquired too late to begin remote reconciliation safely.",
                    cancellationToken);
            using var submissionCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken);
            submissionCancellation.CancelAfter(remainingRemoteBudget);
            client = downloadClientProvider.GetRequiredClient(next.DownloadType);
            if (!lease.IsPrepared)
            {
                preparationAttempted = true;
                await beforeDownloadStartEventTrigger.InvokeAsync(
                    new FileDownloadStartParam(
                        next.Id,
                        next.DownloadUrl,
                        next.CachedDownloadData,
                        next.AdditionalDownloadInfo),
                    submissionCancellation.Token);
                if (!await upgradeRepository.TryMarkDownloadPreparedAsync(
                        operation.Id,
                        leaseId,
                        DateTimeOffset.UtcNow,
                        submissionCancellation.Token))
                    return Result(
                        true,
                        "download_submission_in_progress",
                        false,
                        true,
                        operation,
                        []);
            }

            if (!next.IsDownloadTracked)
            {
                downloadStartAttempted = true;
                if (!await animationInfoRepository.TryStartUpgradeDownloadAsync(
                        next.Id,
                        operation.Id,
                        downloadAttemptId,
                        DateTimeOffset.UtcNow,
                        SubscriptionAutomationDisposition.AutoDownloadQueued,
                        cancellationToken))
                {
                    var racedCandidate = await animationInfoRepository.FindByIdAsync(
                        next.Id,
                        cancellationToken);
                    if (racedCandidate?.IsDownloadFinished == true)
                        return await ActivateAsync(operation.CandidateReleaseId, cancellationToken);
                    if (racedCandidate?.IsDownloadTracked != true ||
                        racedCandidate.DownloadCancellationId is not null ||
                        racedCandidate.DownloadAttemptId is not { } racedAttemptId)
                        return await FailAsync(operation, "Candidate download state changed.", cancellationToken);

                    next = racedCandidate;
                    downloadAttemptId = racedAttemptId;
                    recoveringPersistedSubmission = true;
                }
            }

            submissionAttempted = true;
            var reconciliation = await client.EnsureDownloadTaskAsync(
                next.Id,
                next.DownloadUrl,
                next.CachedDownloadData,
                next.AdditionalDownloadInfo,
                submissionCancellation.Token);
            if (reconciliation == DownloadTaskReconciliationOutcome.Unknown)
            {
                submissionUncertain = true;
                return await RecoveryPendingAsync(
                    operation,
                    "The candidate submission could not yet be confirmed remotely.",
                    cancellationToken);
            }
            if (reconciliation == DownloadTaskReconciliationOutcome.Rejected)
            {
                var compensation = await CompensateDownloadStartAsync(
                    next,
                    client,
                    recoveringPersistedSubmission ? next.DownloadAttemptId : downloadAttemptId,
                    remoteMayHaveAccepted: true);
                if (compensation == DownloadCompensationOutcome.RetainedForRecovery)
                    return await RecoveryPendingAsync(
                        operation,
                        "The remote client rejected the candidate, but durable cancellation is still pending.",
                        cancellationToken);
                return await FailAsync(
                    operation,
                    "The remote client rejected the candidate submission.",
                    cancellationToken);
            }

            if (reconciliation != DownloadTaskReconciliationOutcome.Confirmed)
            {
                submissionUncertain = true;
                return await RecoveryPendingAsync(
                    operation,
                    "The candidate submission remains unresolved.",
                    cancellationToken);
            }

            var markOutcome = await upgradeRepository.TryMarkDownloadSubmittedAsync(
                operation.Id,
                leaseId,
                recoveringPersistedSubmission ? next.DownloadAttemptId : downloadAttemptId,
                DateTimeOffset.UtcNow,
                cancellationToken);
            if (markOutcome == ReleaseUpgradeDownloadSubmissionMarkOutcome.LeaseLost)
                return Result(
                    true,
                    "download_submission_in_progress",
                    false,
                    true,
                    operation,
                    []);
            if (markOutcome is ReleaseUpgradeDownloadSubmissionMarkOutcome.StateChanged or
                ReleaseUpgradeDownloadSubmissionMarkOutcome.NotFound)
            {
                var current = await animationInfoRepository.FindByIdAsync(
                    next.Id,
                    cancellationToken);
                if (current?.DownloadCancellationId is not null)
                    return await RecoveryPendingAsync(
                        operation,
                        "The download was submitted while cancellation was in progress; durable cleanup is pending.",
                        cancellationToken);
                return Result(
                    false,
                    "upgrade_no_longer_active",
                    false,
                    true,
                    operation,
                    ["The download continues independently because the upgrade operation changed state."]);
            }

            return Result(
                true,
                recoveringPersistedSubmission ? "download_submission_recovered" : "download_queued",
                false,
                true,
                operation,
                []);
        }
        catch (HttpRequestException exception)
        {
            logger.LogWarning(
                exception,
                "Remote submission response was uncertain for release upgrade {OperationId}",
                operation.Id);
            return await RecoveryPendingAsync(
                operation,
                $"{exception.Message} Remote submission reconciliation will be retried.",
                cancellationToken);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested &&
                                                 (preparationAttempted || submissionAttempted))
        {
            return await RecoveryPendingAsync(
                operation,
                "The remote submission deadline elapsed; reconciliation will resume after the durable lease expires.",
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            if (recoveringPersistedSubmission || submissionUncertain)
                throw;

            var compensation = downloadStartAttempted
                ? await CompensateDownloadStartAsync(
                    next,
                    client,
                    downloadAttemptId,
                    submissionAttempted)
                : DownloadCompensationOutcome.LocalStateRestored;
            await FinalizeCancelledOperationAsync(operation, compensation);

            throw;
        }
        catch (Exception exception)
        {
            if (recoveringPersistedSubmission || submissionUncertain)
            {
                logger.LogWarning(
                    exception,
                    "Could not reconcile persisted download submission for release upgrade {OperationId}",
                    operation.Id);
                return await RecoveryPendingAsync(
                    operation,
                    $"{exception.Message} The persisted submission will be retried after its lease expires.",
                    cancellationToken);
            }

            var compensation = downloadStartAttempted
                ? await CompensateDownloadStartAsync(
                    next,
                    client,
                    downloadAttemptId,
                    submissionAttempted)
                : DownloadCompensationOutcome.LocalStateRestored;

            logger.LogWarning(exception, "Failed to queue release upgrade {OperationId}", operation.Id);
            if (compensation == DownloadCompensationOutcome.RetainedForRecovery)
                return await RecoveryPendingAsync(
                    operation,
                    $"{exception.Message} Download tracking was retained because cancellation could not be confirmed.",
                    cancellationToken);
            return await FailAsync(operation, exception.Message, cancellationToken);
        }
    }

    public async Task<ReleaseUpgradeMutationResult> RollbackAsync(
        Guid operationId,
        CancellationToken cancellationToken)
    {
        var rolledBackAt = DateTimeOffset.UtcNow;
        var rollback = await upgradeRepository.GetRollbackAsync(operationId, cancellationToken);
        if (rollback?.Operation.RollbackUntil >= rolledBackAt)
        {
            var errors = await ValidateReadableMappingsAsync(
                rollback.PreviousMappings,
                requireVideo: true,
                cancellationToken);
            if (errors.Count > 0)
            {
                var summary = string.Join(" ", errors);
                await incidentReporter.ReportAsync(new IncidentReport(
                        IncidentType.FileMappingFailure,
                        IncidentSeverity.Error,
                        "Release upgrade rollback validation failed",
                        summary,
                        UpgradeSource(operationId)),
                    cancellationToken);
                return new ReleaseUpgradeMutationResult(
                    false,
                    "rollback_validation_failed",
                    rollback.Operation);
            }
        }

        var result = await upgradeRepository.RollbackAsync(
            operationId,
            rolledBackAt,
            cancellationToken);
        if (result.IsSuccess)
        {
            await incidentReporter.ResolveAsync(
                IncidentType.FileMappingFailure,
                UpgradeSource(operationId),
                cancellationToken);
        }

        return result;
    }

    private async Task<ReleaseUpgradeExecutionResult> ActivateAsync(
        Guid candidateReleaseId,
        CancellationToken cancellationToken)
    {
        var activation = await upgradeRepository.GetActivationAsync(
            candidateReleaseId,
            cancellationToken);
        if (activation is null)
            return Result(false, "operation_not_found", false, false, null,
                ["No active upgrade was found for this candidate."]);
        if (activation.CandidateMappings.Count == 0)
            return Result(true, "mapping_pending", false, false, activation.Operation, []);

        // Validation is deliberately external to the mapping transaction. Until every
        // candidate file passes, the old virtual paths and physical files remain untouched.
        var validationErrors = await ValidateActivationAsync(activation, cancellationToken);
        if (validationErrors.Count > 0)
            return await FailAsync(activation.Operation, string.Join(" ", validationErrors), cancellationToken,
                validationErrors);

        var candidate = await animationInfoRepository.FindByIdAsync(candidateReleaseId, cancellationToken);
        var rollbackHours = 72;
        if (candidate?.SourceFeedId is { } feedId)
        {
            var policy = await policyRepository.FindByFeedIdAsync(feedId, cancellationToken);
            rollbackHours = policy?.UpgradeRollbackHours ?? rollbackHours;
        }

        var now = DateTimeOffset.UtcNow;
        var mutation = await upgradeRepository.ActivateAsync(
            activation.Operation.Id,
            activation.PreviousMappings,
            activation.CandidateMappings,
            now,
            now.AddHours(rollbackHours),
            cancellationToken);
        if (!mutation.IsSuccess)
            return await FailAsync(activation.Operation,
                $"Atomic mapping swap failed: {mutation.Outcome}.",
                cancellationToken);

        await incidentReporter.ResolveAsync(
            IncidentType.FileMappingFailure,
            UpgradeSource(activation.Operation.Id),
            cancellationToken);
        return Result(true, mutation.Outcome, false, false, mutation.Operation, []);
    }

    private async Task<IReadOnlyList<string>> ValidateCandidateAsync(
        ReleaseUpgradeCandidate candidate,
        CancellationToken cancellationToken)
    {
        var previous = await fileMappingRepository.GetForAnimationInfoAsync(
            candidate.CurrentReleaseId,
            cancellationToken);
        var next = await upgradeRepository.GetCandidateMappingsAsync(
            candidate.CandidateReleaseId,
            cancellationToken);
        return await ValidateMappingsAsync(previous, next, cancellationToken);
    }

    private async Task<IReadOnlyList<string>> ValidateActivationAsync(
        ReleaseUpgradeActivation activation,
        CancellationToken cancellationToken) =>
        await ValidateMappingsAsync(
            activation.PreviousMappings,
            activation.CandidateMappings,
            cancellationToken);

    private async Task<IReadOnlyList<string>> ValidateMappingsAsync(
        IReadOnlyList<FileMapping> previousMappings,
        IReadOnlyList<FileMapping> candidateMappings,
        CancellationToken cancellationToken)
    {
        var errors = new List<string>();
        if (previousMappings.Count == 0)
            errors.Add("The current release has no mapping to preserve.");
        if (candidateMappings.Count == 0)
            errors.Add("The candidate release has no mapped files.");

        errors.AddRange(await ValidateReadableMappingsAsync(
            candidateMappings,
            requireVideo: true,
            cancellationToken));
        return errors;
    }

    private async Task<IReadOnlyList<string>> ValidateReadableMappingsAsync(
        IReadOnlyList<FileMapping> mappings,
        bool requireVideo,
        CancellationToken cancellationToken)
    {
        var errors = new List<string>();
        var readableFiles = 0;
        var readableVideos = 0;
        foreach (var mapping in mappings)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var store = fileStoreProvider.GetRequiredClient(mapping.FileStore);
                if (!await store.ExistAsync(mapping.PhysicalPath, cancellationToken))
                {
                    errors.Add($"Release file is missing: {mapping.PhysicalPath}");
                    continue;
                }

                var info = await store.FileInfoAsync(mapping.PhysicalPath, cancellationToken);
                if (info.IsDirectory || info.Length is <= 0)
                    errors.Add($"Release file is not a readable non-empty file: {mapping.PhysicalPath}");
                else
                {
                    readableFiles++;
                    if (MediaFileTypes.IsVideo(mapping.PhysicalPath))
                        readableVideos++;
                }
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                errors.Add($"Release file validation failed for {mapping.PhysicalPath}: {exception.Message}");
            }
        }

        if (mappings.Count > 0 && readableFiles == 0)
            errors.Add("Release validation found no readable file.");
        if (requireVideo && mappings.Count > 0 && readableVideos == 0)
            errors.Add("Release validation found no readable video file.");
        return errors;
    }

    private async Task<ReleaseUpgradeExecutionResult> FailAsync(
        ReleaseUpgradeOperation operation,
        string summary,
        CancellationToken cancellationToken,
        IReadOnlyList<string>? errors = null)
    {
        var mutation = await upgradeRepository.MarkFailedAsync(
            operation.Id,
            summary,
            cancellationToken);
        if (!mutation.IsSuccess)
        {
            var settled = mutation.Operation?.Status is
                ReleaseUpgradeStatus.Applied or
                ReleaseUpgradeStatus.Completed or
                ReleaseUpgradeStatus.RolledBack;
            return Result(
                settled,
                settled ? "already_settled" : mutation.Outcome,
                false,
                false,
                mutation.Operation ?? operation,
                errors ?? [summary]);
        }

        await incidentReporter.ReportAsync(new IncidentReport(
                IncidentType.FileMappingFailure,
                IncidentSeverity.Error,
                "Release upgrade failed",
                summary,
                UpgradeSource(operation.Id)),
            cancellationToken);
        return Result(false, "failed", false, false, mutation.Operation ?? operation,
            errors ?? [summary]);
    }

    private async Task<ReleaseUpgradeExecutionResult> RecoveryPendingAsync(
        ReleaseUpgradeOperation operation,
        string summary,
        CancellationToken cancellationToken)
    {
        await incidentReporter.ReportAsync(new IncidentReport(
                IncidentType.FileMappingFailure,
                IncidentSeverity.Error,
                "Release upgrade download recovery pending",
                summary,
                UpgradeSource(operation.Id)),
            cancellationToken);
        return Result(false, "recovery_pending", false, true, operation, [summary]);
    }

    private async Task FinalizeCancelledOperationAsync(
        ReleaseUpgradeOperation operation,
        DownloadCompensationOutcome compensation)
    {
        using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var summary = compensation == DownloadCompensationOutcome.LocalStateRestored
            ? "Release upgrade request was cancelled and its download state was restored."
            : "Release upgrade request was cancelled, but download tracking was retained for recovery.";
        try
        {
            if (compensation == DownloadCompensationOutcome.LocalStateRestored)
                await upgradeRepository.MarkFailedAsync(operation.Id, summary, cleanup.Token);
            await incidentReporter.ReportAsync(new IncidentReport(
                    IncidentType.FileMappingFailure,
                    IncidentSeverity.Error,
                    compensation == DownloadCompensationOutcome.LocalStateRestored
                        ? "Release upgrade cancelled"
                        : "Release upgrade download recovery pending",
                    summary,
                    UpgradeSource(operation.Id)),
                cleanup.Token);
        }
        catch (Exception exception)
        {
            logger.LogWarning(
                exception,
                "Could not finalize cancelled release upgrade {OperationId}",
                operation.Id);
        }
    }

    private async Task<DownloadCompensationOutcome> CompensateDownloadStartAsync(
        AnimationInfo info,
        IFileDownloadClient? downloadClient,
        Guid? downloadAttemptId,
        bool remoteMayHaveAccepted)
    {
        using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var cancellationAttemptId = Guid.NewGuid();
        DownloadCancellationLease? cancellationLease;
        try
        {
            // Register cancellation before any remote I/O. Activation and the
            // cancellation saga share the mapping lock, so an activation that
            // already committed makes this return false and its live files are
            // never deleted by stale compensation.
            cancellationLease = await animationInfoRepository.TryBeginCancelDownloadAsync(
                    info.Id,
                    downloadAttemptId,
                    cancellationAttemptId,
                    Guid.NewGuid(),
                    DownloadCancellationLeaseDuration,
                    removeFile: false,
                    requireUnfinished: true,
                    SubscriptionAutomationDisposition.AutoDownloadFailed,
                    cleanup.Token);
            if (cancellationLease is null)
            {
                var current = await animationInfoRepository.FindByIdAsync(
                    info.Id,
                    cleanup.Token);
                if (current?.DownloadCancellationId is not { } durableCancellationId ||
                    (current.IsDownloadTracked
                        ? current.DownloadAttemptId != downloadAttemptId
                        : current.IsDownloadFinished || current.DownloadAttemptId is not null))
                    return current is null || !current.IsDownloadTracked
                        ? DownloadCompensationOutcome.LocalStateRestored
                        : DownloadCompensationOutcome.RetainedForRecovery;

                cancellationAttemptId = durableCancellationId;
                cancellationLease = await animationInfoRepository.TryBeginCancelDownloadAsync(
                        info.Id,
                        downloadAttemptId,
                        cancellationAttemptId,
                        Guid.NewGuid(),
                        DownloadCancellationLeaseDuration,
                        removeFile: false,
                        requireUnfinished: true,
                        SubscriptionAutomationDisposition.AutoDownloadFailed,
                        cleanup.Token);
                if (cancellationLease is null)
                    return DownloadCompensationOutcome.RetainedForRecovery;
            }
        }
        catch (Exception exception)
        {
            logger.LogWarning(
                exception,
                "Could not register compensation for release upgrade download {AnimationInfoId}",
                info.Id);
            return DownloadCompensationOutcome.RetainedForRecovery;
        }

        if (remoteMayHaveAccepted && downloadClient is not null)
        {
            try
            {
                cleanup.Token.ThrowIfCancellationRequested();
                var cancellation = await downloadClient.CancelDownloadTaskAsync(
                    info.Id,
                    info.DownloadUrl,
                    info.CachedDownloadData,
                    info.AdditionalDownloadInfo,
                    cancellationLease.RemoveFile,
                    cleanup.Token);
                if (!cancellation.IsSuccess)
                {
                    await QueryDownloadProgressSafelyAsync(downloadClient, info, cleanup.Token);
                    return DownloadCompensationOutcome.RetainedForRecovery;
                }
            }
            catch (Exception exception)
            {
                logger.LogWarning(
                    exception,
                    "Could not confirm compensation for release upgrade download {AnimationInfoId}",
                    info.Id);
                await QueryDownloadProgressSafelyAsync(downloadClient, info, cleanup.Token);
                return DownloadCompensationOutcome.RetainedForRecovery;
            }
        }
        else if (remoteMayHaveAccepted)
        {
            return DownloadCompensationOutcome.RetainedForRecovery;
        }

        try
        {
            cleanup.Token.ThrowIfCancellationRequested();
            var restored = await fileMappingRepository.TryFinalizeDownloadCancellationAsync(
                info.Id,
                downloadAttemptId,
                cancellationAttemptId,
                cancellationLease.Id,
                terminalDisposition: null,
                cleanup.Token);
            if (restored)
                return DownloadCompensationOutcome.LocalStateRestored;

            var current = await animationInfoRepository.FindByIdAsync(info.Id, cleanup.Token);
            return current is null || !current.IsDownloadTracked
                ? DownloadCompensationOutcome.LocalStateRestored
                : DownloadCompensationOutcome.RetainedForRecovery;
        }
        catch (Exception exception)
        {
            logger.LogWarning(
                exception,
                "Could not restore local download state for release upgrade {AnimationInfoId}",
                info.Id);
            return DownloadCompensationOutcome.RetainedForRecovery;
        }
    }

    private static async Task QueryDownloadProgressSafelyAsync(
        IFileDownloadClient downloadClient,
        AnimationInfo info,
        CancellationToken cancellationToken)
    {
        try
        {
            await downloadClient.SubmitQueryDownloadProgressAsync(
                info.Id,
                info.DownloadUrl,
                info.CachedDownloadData,
                info.AdditionalDownloadInfo,
                cancellationToken);
        }
        catch
        {
            // Startup recovery can rediscover the persisted attempt.
        }
    }

    private static CancellationTokenSource? CreateLeaseBoundTokenSource(
        CancellationToken cancellationToken,
        long leaseRequestStartedAt,
        TimeSpan leaseDuration)
    {
        var remaining = leaseDuration -
                        Stopwatch.GetElapsedTime(leaseRequestStartedAt) -
                        DownloadLeaseSafetyMargin;
        if (remaining <= TimeSpan.Zero)
            return null;
        var tokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        tokenSource.CancelAfter(
            remaining < TimeSpan.FromSeconds(10) ? remaining : TimeSpan.FromSeconds(10));
        return tokenSource;
    }

    private static string UpgradeSource(Guid operationId) => $"release-upgrade:{operationId:N}";

    private enum DownloadCompensationOutcome
    {
        LocalStateRestored,
        RetainedForRecovery
    }

    private static ReleaseUpgradeExecutionResult Result(
        bool success,
        string outcome,
        bool dryRun,
        bool requiresDownload,
        ReleaseUpgradeOperation? operation,
        IReadOnlyList<string> errors) =>
        new(success, outcome, dryRun, requiresDownload, operation, errors);
}
