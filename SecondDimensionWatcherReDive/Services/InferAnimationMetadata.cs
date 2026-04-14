using SecondDimensionWatcherReDive.Framework.Inference;
using SecondDimensionWatcherReDive.Framework.DataRepository;
using SecondDimensionWatcherReDive.Framework.Tasks;
using SecondDimensionWatcherReDive.Inference.AI.Tools;

namespace SecondDimensionWatcherReDive.Services;

/// <summary>
///     Scheduled task that performs offline AI inference on AnimationInfo records
///     that have not yet been processed.
/// </summary>
public partial class InferAnimationMetadata(
    IServiceScopeFactory scopeFactory,
    TmdbTool tmdbTool,
    ILogger<InferAnimationMetadata> logger)
    : ScheduledTaskBase
{
    private const int MaxRetryCount = 3;

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

        var pendingItems = await animationInfoRepository.GetPendingInferenceAsync(MaxRetryCount, cancellationToken);

        if (pendingItems.Count > 0)
            LogFoundPendingItems(logger, pendingItems.Count);

        foreach (var item in pendingItems)
        {
            await ProcessItem(item, animationInfoRepository, animationRepository, animationGroupRepository,
                inferenceEngine, cancellationToken);
        }
    }

    private async Task ProcessItem(
        AnimationInfo item,
        IAnimationInfoRepository animationInfoRepository,
        IAnimationRepository animationRepository,
        IAnimationGroupRepository animationGroupRepository,
        IInferenceEngine inferenceEngine,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await inferenceEngine.InferAsync(
                item.Title, item.Description, cancellationToken);

            if (result != null)
            {
                item = item with { Season = result.Season, Episode = result.Episode };

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
            }

            item = item with { IsAiProcessed = true };
            await animationInfoRepository.UpdateAsync(item, cancellationToken);

            LogInferenceCompleted(logger, item.Id, item.Title);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            item = item with { AiRetryCount = item.AiRetryCount + 1 };
            await animationInfoRepository.UpdateAsync(item, cancellationToken);

            LogInferenceFailed(logger, ex, item.Id, item.Title, item.AiRetryCount, MaxRetryCount);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Found {Count} AnimationInfo records pending AI inference")]
    private static partial void LogFoundPendingItems(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Information, Message = "AI inference completed for AnimationInfo {Id}: {Title}")]
    private static partial void LogInferenceCompleted(ILogger logger, Guid id, string title);

    [LoggerMessage(Level = LogLevel.Warning, Message = "AI inference failed for AnimationInfo {Id}: {Title} (retry {RetryCount}/{MaxRetry})")]
    private static partial void LogInferenceFailed(ILogger logger, Exception ex, Guid id, string title, int retryCount, int maxRetry);
}
