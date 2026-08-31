using SecondDimensionWatcherReDive.Framework.DataRepository;
using SecondDimensionWatcherReDive.Framework.FileDownload;
using SecondDimensionWatcherReDive.Framework.Inference;
using SecondDimensionWatcherReDive.Framework.Tasks;
using SecondDimensionWatcherReDive.Utils.FileStore;
using SecondDimensionWatcherReDive.Utils.ReleaseUpgrades;

namespace SecondDimensionWatcherReDive.Utils.Incidents;

public sealed partial class IncidentRetryService(
    IIncidentRepository incidentRepository,
    IServiceScopeFactory scopeFactory,
    IEnumerable<IScheduledTask> scheduledTasks,
    IIncidentDiskProbe diskProbe,
    ILogger<IncidentRetryService> logger) : IIncidentRetryService
{
    private const string ReleaseUpgradeSourcePrefix = "release-upgrade:";

    public async Task<IncidentRetryResult?> RetryAsync(
        Guid id,
        CancellationToken cancellationToken)
    {
        var incident = await incidentRepository.FindByIdAsync(id, cancellationToken);
        if (incident is null) return null;
        if (incident.ResolvedAt is not null)
            return new IncidentRetryResult(id, "resolved", false, incident, "Incident is already resolved.");

        return await RetryIncidentAsync(incident, enqueueScheduledTask: true, cancellationToken);
    }

    public async Task<IncidentRetryBatchResult> RetryAllAsync(CancellationToken cancellationToken)
    {
        var incidents = await incidentRepository.GetOpenAsync(null, cancellationToken);
        var results = new List<IncidentRetryResult>(incidents.Count);
        var sharedTaskTypesToQueue = new HashSet<IncidentType>();
        foreach (var incident in incidents)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var usesSharedTask = incident.Type is IncidentType.FeedFailure or IncidentType.AiFailure;
            // Reset every AI item before waking the shared inference task, otherwise
            // the queue consumer could snapshot pending work between two resets.
            var result = await RetryIncidentAsync(
                incident,
                enqueueScheduledTask: !usesSharedTask,
                cancellationToken);
            results.Add(result);
            if (usesSharedTask && result.IsSuccess)
                sharedTaskTypesToQueue.Add(incident.Type);
        }

        foreach (var type in sharedTaskTypesToQueue)
        {
            _ = type switch
            {
                IncidentType.FeedFailure => RetryScheduledTask("SyncFeed", enqueue: true),
                IncidentType.AiFailure => RetryScheduledTask("InferAnimationMetadata", enqueue: true),
                _ => throw new ArgumentOutOfRangeException(nameof(type), type, null)
            };
        }

        return new IncidentRetryBatchResult(
            results.Count,
            results.Count(result => result.IsSuccess),
            results.Count(result => !result.IsSuccess),
            results);
    }

    private async Task<IncidentRetryResult> RetryIncidentAsync(
        Incident incident,
        bool enqueueScheduledTask,
        CancellationToken cancellationToken)
    {

        try
        {
            var status = incident.Type switch
            {
                IncidentType.FeedFailure => enqueueScheduledTask
                    ? RetryScheduledTask("SyncFeed", enqueue: true)
                    : RetryScheduledTask("SyncFeed", enqueue: false),
                IncidentType.AiFailure => await RetryAiAsync(
                    incident,
                    enqueueScheduledTask,
                    cancellationToken),
                IncidentType.FileMappingFailure => await RetryFileMappingAsync(incident, cancellationToken),
                IncidentType.DownloadStalled => await RetryDownloadAsync(incident, cancellationToken),
                IncidentType.DiskSpaceLow => await RetryDiskAsync(cancellationToken),
                _ => throw new ArgumentOutOfRangeException(nameof(incident.Type), incident.Type, null)
            };

            var queued = string.Equals(status, "queued", StringComparison.Ordinal);
            var updated = await incidentRepository.RecordRetryAsync(
                incident.Id,
                DateTimeOffset.UtcNow,
                null,
                resolve: !queued,
                cancellationToken);
            return new IncidentRetryResult(incident.Id, status, true, updated, null);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            var error = LimitError(ex.Message);
            var updated = await incidentRepository.RecordRetryAsync(
                incident.Id,
                DateTimeOffset.UtcNow,
                error,
                resolve: false,
                cancellationToken);
            LogRetryFailed(logger, ex, incident.Id, incident.Type);
            return new IncidentRetryResult(incident.Id, "failed", false, updated, error);
        }
    }

    private string RetryScheduledTask(string id, bool enqueue)
    {
        var task = scheduledTasks.FirstOrDefault(candidate =>
            string.Equals(candidate.Id, id, StringComparison.OrdinalIgnoreCase));
        if (task is null)
            throw new InvalidOperationException($"Scheduled task '{id}' is not available.");

        if (enqueue) task.Enqueue();
        return "queued";
    }

    private async Task<string> RetryAiAsync(
        Incident incident,
        bool enqueueScheduledTask,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(incident.SourceId, out var animationInfoId))
            throw new InvalidOperationException("AI incident source is not a valid animation id.");

        // Validate task availability before changing persistent retry state.
        var task = scheduledTasks.FirstOrDefault(candidate =>
            string.Equals(candidate.Id, "InferAnimationMetadata", StringComparison.OrdinalIgnoreCase));
        if (task is null || !task.IsEnabled)
            throw new InvalidOperationException("AI inference is not configured.");

        await using var scope = scopeFactory.CreateAsyncScope();
        var repository = scope.ServiceProvider.GetRequiredService<IAnimationInfoRepository>();
        var info = await repository.FindByIdAsync(animationInfoId, cancellationToken)
                   ?? throw new InvalidOperationException("Animation no longer exists.");
        await repository.UpdateAsync(info with
        {
            IsAiProcessed = false,
            AiRetryCount = 0,
            MetadataStatus = MetadataReviewStatus.Pending,
            MetadataConfidence = null,
            MetadataLastError = null,
            MetadataReviewedAt = null
        }, cancellationToken);
        if (enqueueScheduledTask) task.Enqueue();
        return "queued";
    }

    private async Task<string> RetryFileMappingAsync(
        Incident incident,
        CancellationToken cancellationToken)
    {
        if (TryParseReleaseUpgradeSource(incident.SourceId, out var operationId))
            return await RetryReleaseUpgradeAsync(operationId, cancellationToken);

        if (!Guid.TryParse(incident.SourceId, out var animationInfoId))
            throw new InvalidOperationException("Mapping incident source is not a valid animation id.");

        await using var scope = scopeFactory.CreateAsyncScope();
        var mapper = scope.ServiceProvider.GetRequiredService<IFileMapper>();
        if (!await mapper.MapDownloadAsync(animationInfoId, cancellationToken))
            throw new InvalidOperationException("No file mapping could be produced.");
        return "resolved";
    }

    private async Task<string> RetryReleaseUpgradeAsync(
        Guid operationId,
        CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var repository = scope.ServiceProvider.GetRequiredService<IReleaseUpgradeRepository>();
        var coordinator = scope.ServiceProvider.GetRequiredService<IReleaseUpgradeCoordinator>();
        var operation = await repository.FindByIdAsync(operationId, cancellationToken);
        for (var stateRead = 0; stateRead < 2; stateRead++)
        {
            if (operation is null)
                return "resolved";

            switch (operation.Status)
            {
                case ReleaseUpgradeStatus.Downloading:
                case ReleaseUpgradeStatus.Verifying:
                    {
                        var result = await coordinator.TryActivateCandidateAsync(
                            operation.CandidateReleaseId,
                            cancellationToken);
                        if (result is not null)
                            return NormalizeReleaseUpgradeRetry(result);

                        operation = await repository.FindByIdAsync(operationId, cancellationToken);
                        continue;
                    }
                case ReleaseUpgradeStatus.Failed:
                    {
                        var candidate = await repository.FindCandidateAsync(
                            operation.CurrentReleaseId,
                            operation.CandidateReleaseId,
                            cancellationToken);
                        if (candidate is null)
                            return "resolved";

                        var result = await coordinator.ExecuteAsync(
                            candidate,
                            dryRun: false,
                            cancellationToken);
                        return NormalizeReleaseUpgradeRetry(result);
                    }
                case ReleaseUpgradeStatus.Applied:
                    {
                        if (operation.RollbackUntil is not { } rollbackUntil ||
                            rollbackUntil < DateTimeOffset.UtcNow)
                            return "resolved";

                        var result = await coordinator.RollbackAsync(operation.Id, cancellationToken);
                        if (!result.IsSuccess)
                            throw new InvalidOperationException(
                                $"Release upgrade rollback retry failed: {result.Outcome}.");
                        return "resolved";
                    }
                case ReleaseUpgradeStatus.RolledBack:
                case ReleaseUpgradeStatus.Completed:
                    return "resolved";
                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(operation.Status),
                        operation.Status,
                        null);
            }
        }

        return "queued";
    }

    private static string NormalizeReleaseUpgradeRetry(ReleaseUpgradeExecutionResult result)
    {
        if (!result.IsSuccess)
        {
            if (result.Outcome is "recovery_pending" or "upgrade_already_started")
                return "queued";

            var detail = result.ValidationErrors.FirstOrDefault();
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(detail)
                ? $"Release upgrade retry failed: {result.Outcome}."
                : detail);
        }

        return result.Outcome is
            "download_queued" or
            "download_in_progress" or
            "download_submission_in_progress" or
            "download_submission_recovered" or
            "mapping_pending"
                ? "queued"
                : "resolved";
    }

    private static bool TryParseReleaseUpgradeSource(string sourceId, out Guid operationId)
    {
        if (sourceId.StartsWith(ReleaseUpgradeSourcePrefix, StringComparison.Ordinal) &&
            Guid.TryParseExact(
                sourceId.AsSpan(ReleaseUpgradeSourcePrefix.Length),
                "N",
                out operationId))
            return true;

        operationId = default;
        return false;
    }

    private async Task<string> RetryDownloadAsync(
        Incident incident,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(incident.SourceId, out var animationInfoId))
            throw new InvalidOperationException("Download incident source is not a valid animation id.");

        await using var scope = scopeFactory.CreateAsyncScope();
        var repository = scope.ServiceProvider.GetRequiredService<IAnimationInfoRepository>();
        var provider = scope.ServiceProvider.GetRequiredService<IFileDownloadClientProvider>();
        var info = await repository.FindByIdAsync(animationInfoId, cancellationToken)
                   ?? throw new InvalidOperationException("Animation no longer exists.");
        if (!info.IsDownloadTracked || info.IsDownloadFinished)
            return "resolved";

        var client = provider.GetRequiredClient(info.DownloadType);
        if (!await client.ResumeDownloadTaskAsync(
                info.Id,
                info.DownloadUrl,
                info.CachedDownloadData,
                info.AdditionalDownloadInfo,
                cancellationToken))
            throw new InvalidOperationException("Download client rejected the resume request.");

        await client.SubmitQueryDownloadProgressAsync(
            info.Id,
            info.DownloadUrl,
            info.CachedDownloadData,
            info.AdditionalDownloadInfo,
            cancellationToken);
        // A successful resume request only proves that the remote client accepted
        // the command. Keep the incident open until the tracker observes real
        // progress, transfer speed, pause, or completion and resolves it.
        return "queued";
    }

    private async Task<string> RetryDiskAsync(CancellationToken cancellationToken)
    {
        var result = await diskProbe.ProbeAsync(cancellationToken);
        if (!result.IsHealthy)
            throw new InvalidOperationException(result.Detail);
        return "resolved";
    }

    private static string LimitError(string? error)
    {
        var value = string.IsNullOrWhiteSpace(error) ? "Retry failed." : error.Trim();
        return value.Length <= 2048 ? value : value[..2048];
    }

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Retry failed for incident {IncidentId} ({IncidentType})")]
    private static partial void LogRetryFailed(
        ILogger logger,
        Exception exception,
        Guid incidentId,
        IncidentType incidentType);
}
