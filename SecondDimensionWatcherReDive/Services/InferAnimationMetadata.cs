using Microsoft.EntityFrameworkCore;
using SecondDimensionWatcherReDive.Framework.Inference;
using SecondDimensionWatcherReDive.Framework.Tasks;
using SecondDimensionWatcherReDive.Inference.AI.Tools;
using SecondDimensionWatcherReDive.Models;

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
        await using var applicationContext = scope.ServiceProvider.GetRequiredService<ApplicationContext>();
        var inferenceEngine = scope.ServiceProvider.GetRequiredService<IInferenceEngine>();

        var pendingItems = await applicationContext.AnimationInfo
            .Where(i => !i.IsAiProcessed && i.AiRetryCount < MaxRetryCount)
            .OrderBy(i => i.PublishTime)
            .ToListAsync(cancellationToken);

        if (pendingItems.Count > 0)
            LogFoundPendingItems(logger, pendingItems.Count);

        foreach (var item in pendingItems)
        {
            await ProcessItem(item, applicationContext, inferenceEngine, cancellationToken);
        }
    }

    private async Task ProcessItem(
        AnimationInfo item,
        ApplicationContext applicationContext,
        IInferenceEngine inferenceEngine,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await inferenceEngine.InferAsync(
                item.Title, item.Description, cancellationToken);

            if (result != null)
            {
                item.Season = result.Season;
                item.Episode = result.Episode;

                // Fetch localized name/description from TMDB
                if (!string.IsNullOrEmpty(result.TmdbId) && int.TryParse(result.TmdbId, out var tmdbIdInt))
                {
                    var details = await tmdbTool.GetLocalizedDetailsAsync(tmdbIdInt, cancellationToken);

                    if (details != null && !string.IsNullOrEmpty(details.Overview))
                        item.Description = details.Overview;

                    var animation = await applicationContext.Animations
                        .FirstOrDefaultAsync(
                            a => a.TmdbId == result.TmdbId,
                            cancellationToken);

                    if (animation == null)
                    {
                        animation = new Animation
                        {
                            TmdbId = result.TmdbId,
                            Name = details?.Name ?? result.TmdbId,
                            OriginalName = details?.OriginalName ?? "",
                            PosterPath = details?.PosterPath
                        };
                        await applicationContext.Animations.AddAsync(animation, cancellationToken);
                    }

                    item.Animation = animation;
                }

                // Resolve or create AnimationGroup
                if (!string.IsNullOrEmpty(result.GroupName))
                {
                    var group = await applicationContext.AnimationGroups
                        .FirstOrDefaultAsync(
                            g => g.Name == result.GroupName,
                            cancellationToken);

                    if (group == null)
                    {
                        group = new AnimationGroup { Name = result.GroupName };
                        await applicationContext.AnimationGroups.AddAsync(group, cancellationToken);
                    }

                    item.Group = group;
                }
            }

            item.IsAiProcessed = true;
            await applicationContext.SaveChangesAsync(cancellationToken);

            LogInferenceCompleted(logger, item.Id, item.Title);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            item.AiRetryCount++;
            await applicationContext.SaveChangesAsync(cancellationToken);

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
