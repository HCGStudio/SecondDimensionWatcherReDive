using SecondDimensionWatcherReDive.Framework.DataRepository;
using SecondDimensionWatcherReDive.Framework.FileDownload;
using SecondDimensionWatcherReDive.Framework.FileStore;
using SecondDimensionWatcherReDive.Utils.Incidents;

namespace SecondDimensionWatcherReDive.Utils.ReleaseUpgrades;

public sealed class ReleaseUpgradeCoordinator(
    IReleaseUpgradeRepository upgradeRepository,
    IAnimationInfoRepository animationInfoRepository,
    ISubscriptionAutomationPolicyRepository policyRepository,
    IFileMappingRepository fileMappingRepository,
    IFileDownloadClientProvider downloadClientProvider,
    IFileStoreProvider fileStoreProvider,
    IIncidentReporter incidentReporter,
    ILogger<ReleaseUpgradeCoordinator> logger) : IReleaseUpgradeCoordinator
{
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

        if (!requiresDownload)
            return await ActivateAsync(operation.CandidateReleaseId, cancellationToken);

        var downloadAttemptId = Guid.NewGuid();
        try
        {
            if (!await animationInfoRepository.TryStartDownloadAsync(
                    next.Id,
                    downloadAttemptId,
                    DateTimeOffset.UtcNow,
                    SubscriptionAutomationDisposition.AutoDownloadQueued,
                    cancellationToken))
                return await FailAsync(operation, "Candidate download state changed.", cancellationToken);

            var client = downloadClientProvider.GetRequiredClient(next.DownloadType);
            if (!await client.SubmitDownloadTaskAsync(
                    next.Id,
                    next.DownloadUrl,
                    next.CachedDownloadData,
                    next.AdditionalDownloadInfo,
                    cancellationToken))
            {
                await animationInfoRepository.TryCancelDownloadAsync(
                    next.Id,
                    downloadAttemptId,
                    SubscriptionAutomationDisposition.AutoDownloadFailed,
                    cancellationToken);
                return await FailAsync(operation, "Download client rejected the candidate.", cancellationToken);
            }

            return Result(true, "download_queued", false, true, operation, []);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Failed to queue release upgrade {OperationId}", operation.Id);
            return await FailAsync(operation, exception.Message, cancellationToken);
        }
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
            : await ActivateAsync(candidateReleaseId, cancellationToken);
    }

    public async Task<ReleaseUpgradeMutationResult> RollbackAsync(
        Guid operationId,
        CancellationToken cancellationToken)
    {
        var result = await upgradeRepository.RollbackAsync(
            operationId,
            DateTimeOffset.UtcNow,
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
        var next = await fileMappingRepository.GetForAnimationInfoAsync(
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

        var readableFiles = 0;
        foreach (var mapping in candidateMappings)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var store = fileStoreProvider.GetRequiredClient(mapping.FileStore);
                if (!await store.ExistAsync(mapping.PhysicalPath, cancellationToken))
                {
                    errors.Add($"Candidate file is missing: {mapping.PhysicalPath}");
                    continue;
                }

                var info = await store.FileInfoAsync(mapping.PhysicalPath, cancellationToken);
                if (info.IsDirectory || info.Length is <= 0)
                    errors.Add($"Candidate file is not a readable non-empty file: {mapping.PhysicalPath}");
                else
                    readableFiles++;
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                errors.Add($"Candidate file validation failed for {mapping.PhysicalPath}: {exception.Message}");
            }
        }

        if (candidateMappings.Count > 0 && readableFiles == 0)
            errors.Add("Candidate validation found no readable file.");
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

    private static string UpgradeSource(Guid operationId) => $"release-upgrade:{operationId:N}";

    private static ReleaseUpgradeExecutionResult Result(
        bool success,
        string outcome,
        bool dryRun,
        bool requiresDownload,
        ReleaseUpgradeOperation? operation,
        IReadOnlyList<string> errors) =>
        new(success, outcome, dryRun, requiresDownload, operation, errors);
}
