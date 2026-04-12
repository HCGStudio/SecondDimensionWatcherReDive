using Microsoft.EntityFrameworkCore;
using SecondDimensionWatcherReDive.Framework.Inference;
using SecondDimensionWatcherReDive.Models;

namespace SecondDimensionWatcherReDive.Services;

/// <summary>
///     Background service that performs offline AI inference on AnimationInfo records
///     that have not yet been processed.
/// </summary>
public class InferAnimationMetadata(
    IServiceScopeFactory scopeFactory,
    ILogger<InferAnimationMetadata> logger)
    : BackgroundService
{
    private const int MaxRetryCount = 3;

    protected override async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await ProcessPendingItems(cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Unexpected error in InferAnimationMetadata loop");
            }

            await Task.Delay(TimeSpan.FromMinutes(30), cancellationToken);
        }
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
            logger.LogInformation("Found {Count} AnimationInfo records pending AI inference", pendingItems.Count);

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

                if (!string.IsNullOrEmpty(result.Description))
                    item.Description = result.Description;

                // Resolve or create Animation
                if (!string.IsNullOrEmpty(result.TmdbId))
                {
                    var animation = await applicationContext.Animations
                        .FirstOrDefaultAsync(
                            a => a.TmdbId == result.TmdbId,
                            cancellationToken);

                    if (animation == null)
                    {
                        animation = new Animation
                        {
                            TmdbId = result.TmdbId,
                            Name = result.AnimationName,
                            OriginalName = result.OriginalName
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

            logger.LogInformation(
                "AI inference completed for AnimationInfo {Id}: {Title}",
                item.Id, item.Title);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            item.AiRetryCount++;
            await applicationContext.SaveChangesAsync(cancellationToken);

            logger.LogWarning(ex,
                "AI inference failed for AnimationInfo {Id}: {Title} (retry {RetryCount}/{MaxRetry})",
                item.Id, item.Title, item.AiRetryCount, MaxRetryCount);
        }
    }
}
