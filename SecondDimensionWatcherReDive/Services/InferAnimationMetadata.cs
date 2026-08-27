using SecondDimensionWatcherReDive.Framework.Inference;
using SecondDimensionWatcherReDive.Framework.DataRepository;
using SecondDimensionWatcherReDive.Framework.Tasks;
using SecondDimensionWatcherReDive.Inference.AI.Tools;
using SecondDimensionWatcherReDive.Utils.FileStore;
using SecondDimensionWatcherReDive.Utils.Incidents;

namespace SecondDimensionWatcherReDive.Services;

/// <summary>
///     Scheduled task that performs offline AI inference on AnimationInfo records
///     that have not yet been processed.
/// </summary>
public partial class InferAnimationMetadata(
    IServiceScopeFactory scopeFactory,
    TmdbTool tmdbTool,
    ILogger<InferAnimationMetadata> logger,
    IIncidentReporter? incidentReporter = null)
    : ScheduledTaskBase
{
    private const int MaxRetryCount = 3;
    private const double LowConfidenceThreshold = 0.75;
    private const int MaxErrorLength = 1024;

    public override string Id => "InferAnimationMetadata";
    public override TimeSpan Interval => TimeSpan.FromMinutes(30);

    protected override Task ExecuteTaskAsync(CancellationToken cancellationToken)
    {
        return ProcessPendingItems(cancellationToken);
    }

    private async Task ProcessPendingItems(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var animationInfoRepository = scope.ServiceProvider.GetRequiredService<IAnimationInfoRepository>();
        var animationRepository = scope.ServiceProvider.GetRequiredService<IAnimationRepository>();
        var animationGroupRepository = scope.ServiceProvider.GetRequiredService<IAnimationGroupRepository>();
        var inferenceEngine = scope.ServiceProvider.GetRequiredService<IInferenceEngine>();
        var fileMapper = scope.ServiceProvider.GetRequiredService<IFileMapper>();

        var pendingItems = await animationInfoRepository.GetPendingInferenceAsync(MaxRetryCount, cancellationToken);

        if (pendingItems.Count > 0)
            LogFoundPendingItems(logger, pendingItems.Count);

        foreach (var item in pendingItems)
        {
            await ProcessItem(item, animationInfoRepository, animationRepository, animationGroupRepository,
                inferenceEngine, fileMapper, cancellationToken);
        }
    }

    private async Task ProcessItem(
        AnimationInfo item,
        IAnimationInfoRepository animationInfoRepository,
        IAnimationRepository animationRepository,
        IAnimationGroupRepository animationGroupRepository,
        IInferenceEngine inferenceEngine,
        IFileMapper fileMapper,
        CancellationToken cancellationToken)
    {
        var originalItem = item;
        var expectedStateVersion = item.StateVersion;
        try
        {
            var result = await inferenceEngine.InferAsync(
                item.Title, item.Description, cancellationToken);

            if (result is null)
                throw new InvalidOperationException("Inference returned no usable metadata result.");

            item = originalItem with
            {
                Season = result.Season,
                Episode = result.Episode,
                MetadataConfidence = result.Confidence
            };

            // Fetch localized name/description from TMDB
            if (!string.IsNullOrEmpty(result.TmdbId) && int.TryParse(result.TmdbId, out var tmdbIdInt))
            {
                var details = await tmdbTool.GetLocalizedDetailsAsync(tmdbIdInt, cancellationToken);

                if (details != null && !string.IsNullOrEmpty(details.Overview))
                    item = item with { Description = details.Overview };

                var animation = await animationRepository
                    .FindByTmdbIdAsync(result.TmdbId, cancellationToken);

                if (animation == null)
                {
                    animation = new Animation(
                        Guid.NewGuid(),
                        result.TmdbId,
                        details?.Name ?? result.TmdbId,
                        details?.OriginalName ?? "",
                        details?.PosterPath);
                    await animationRepository.AddAsync(animation, cancellationToken);
                }

                item = item with { Animation = animation };
            }

            // Resolve or create AnimationGroup
            if (!string.IsNullOrEmpty(result.GroupName))
            {
                var group = await animationGroupRepository
                    .FindByNameAsync(result.GroupName, cancellationToken);

                if (group == null)
                {
                    group = new AnimationGroup(Guid.NewGuid(), result.GroupName);
                    await animationGroupRepository.AddAsync(group, cancellationToken);
                }

                item = item with { Group = group };
            }

            var confidence = result.Confidence ?? 0;
            item = item with
            {
                IsAiProcessed = true,
                AiRetryCount = 0,
                MetadataStatus = confidence < LowConfidenceThreshold
                    ? MetadataReviewStatus.LowConfidence
                    : MetadataReviewStatus.Identified,
                MetadataLastError = null,
                MetadataReviewedAt = null
            };
            if (!await animationInfoRepository.TryUpdateAsync(
                    item,
                    expectedStateVersion,
                    cancellationToken))
            {
                LogStaleInferenceDiscarded(logger, item.Id);
                return;
            }

            LogInferenceCompleted(logger, item.Id, item.Title);

            if (incidentReporter is not null)
            {
                await incidentReporter.ResolveAsync(
                    IncidentType.AiFailure,
                    item.Id.ToString(),
                    cancellationToken);
            }

            // Inference may have populated path-defining metadata (Animation, Group,
            // Season, Episode). If the file is already on disk, rebuild its mappings
            // so the canonical virtual path replaces the prior /unknown/... fallback.
            if (item.IsDownloadFinished)
            {
                try
                {
                    if (!await fileMapper.MapDownloadAsync(item.Id, cancellationToken))
                        throw new InvalidOperationException("No file mapping could be produced.");

                    if (incidentReporter is not null)
                    {
                        await incidentReporter.ResolveAsync(
                            IncidentType.FileMappingFailure,
                            item.Id.ToString(),
                            cancellationToken);
                    }
                }
                catch (Exception ex)
                {
                    LogRemapFailed(logger, ex, item.Id);
                    if (incidentReporter is not null)
                    {
                        await incidentReporter.ReportAsync(new IncidentReport(
                                IncidentType.FileMappingFailure,
                                IncidentSeverity.Error,
                                "Downloaded files could not be remapped",
                                ex.Message,
                                item.Id.ToString()),
                            cancellationToken);
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            var retryCount = item.AiRetryCount + 1;
            var error = string.IsNullOrWhiteSpace(ex.Message)
                ? ex.GetType().Name
                : ex.Message;
            if (error.Length > MaxErrorLength) error = error[..MaxErrorLength];
            item = originalItem with
            {
                IsAiProcessed = false,
                AiRetryCount = retryCount,
                MetadataStatus = retryCount >= MaxRetryCount
                    ? MetadataReviewStatus.Failed
                    : MetadataReviewStatus.Pending,
                MetadataConfidence = null,
                MetadataLastError = error,
                MetadataReviewedAt = null
            };
            if (!await animationInfoRepository.TryUpdateAsync(
                    item,
                    expectedStateVersion,
                    cancellationToken))
            {
                LogStaleInferenceDiscarded(logger, item.Id);
                return;
            }

            LogInferenceFailed(logger, ex, item.Id, item.Title, item.AiRetryCount, MaxRetryCount);
            if (retryCount >= MaxRetryCount && incidentReporter is not null)
            {
                await incidentReporter.ReportAsync(new IncidentReport(
                        IncidentType.AiFailure,
                        IncidentSeverity.Error,
                        "AI metadata inference failed",
                        error,
                        item.Id.ToString()),
                    cancellationToken);
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Found {Count} AnimationInfo records pending AI inference")]
    private static partial void LogFoundPendingItems(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Information, Message = "AI inference completed for AnimationInfo {Id}: {Title}")]
    private static partial void LogInferenceCompleted(ILogger logger, Guid id, string title);

    [LoggerMessage(Level = LogLevel.Warning, Message = "AI inference failed for AnimationInfo {Id}: {Title} (retry {RetryCount}/{MaxRetry})")]
    private static partial void LogInferenceFailed(ILogger logger, Exception ex, Guid id, string title, int retryCount, int maxRetry);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Re-mapping files after inference failed for AnimationInfo {Id}")]
    private static partial void LogRemapFailed(ILogger logger, Exception ex, Guid id);

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Discarded stale AI inference result for AnimationInfo {Id} because metadata changed concurrently")]
    private static partial void LogStaleInferenceDiscarded(ILogger logger, Guid id);
}
