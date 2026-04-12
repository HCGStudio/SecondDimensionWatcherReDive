using Microsoft.EntityFrameworkCore;
using SecondDimensionWatcherReDive.Framework.Inference;
using SecondDimensionWatcherReDive.Framework.Tasks;
using SecondDimensionWatcherReDive.Inference.AI.Tools;
using SecondDimensionWatcherReDive.Models;

namespace SecondDimensionWatcherReDive.Services;

/// <summary>
///     Background service that performs offline AI inference on AnimationInfo records
///     that have not yet been processed.
/// </summary>
public class InferAnimationMetadata(
    IServiceScopeFactory scopeFactory,
    TmdbTool tmdbTool,
    ILogger<InferAnimationMetadata> logger)
    : BackgroundService, IScheduledTask
{
    private const int MaxRetryCount = 3;
    private volatile bool _isRunning;
    private DateTimeOffset? _lastRunAt;

    public string Name => "InferAnimationMetadata";
    public string Description => "AI 元数据推断";
    public TimeSpan Interval => TimeSpan.FromMinutes(30);
    public bool IsEnabled => true;
    public DateTimeOffset? LastRunAt => _lastRunAt;
    public bool IsRunning => _isRunning;

    protected override async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await RunNowAsync(cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Unexpected error in InferAnimationMetadata loop");
            }

            await Task.Delay(Interval, cancellationToken);
        }
    }

    public async Task RunNowAsync(CancellationToken cancellationToken)
    {
        _isRunning = true;
        try
        {
            await ProcessPendingItems(cancellationToken);
            _lastRunAt = DateTimeOffset.UtcNow;
        }
        finally
        {
            _isRunning = false;
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
                            OriginalName = details?.OriginalName ?? ""
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
