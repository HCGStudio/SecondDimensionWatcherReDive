using SecondDimensionWatcherReDive.Framework.DataRepository;
using SecondDimensionWatcherReDive.Framework.Tasks;
using SecondDimensionWatcherReDive.Utils.FileStore;

namespace SecondDimensionWatcherReDive.MigrationTasks;

public partial class MigrateFileMappings(
    IServiceScopeFactory scopeFactory,
    ILogger<MigrateFileMappings> logger) : IMigrationTask
{
    private const int PageSize = 50;
    private const int InferenceMaxRetryCount = 3;

    public string Key => "MigrateFileMappings";

    public async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var animationInfoRepository = scope.ServiceProvider.GetRequiredService<IAnimationInfoRepository>();
        var fileMappingRepository = scope.ServiceProvider.GetRequiredService<IFileMappingRepository>();
        var fileMapper = scope.ServiceProvider.GetRequiredService<IFileMapper>();

        var skip = 0;
        var migrated = 0;
        var failed = 0;

        while (!cancellationToken.IsCancellationRequested)
        {
            var page = await animationInfoRepository.GetDownloadedPagedAsync(skip, PageSize, cancellationToken);
            if (page.Data.Count == 0) break;

            var advancedSkip = 0;
            foreach (var info in page.Data)
            {
                if (info.FileStore is null || info.StorePath is null)
                {
                    advancedSkip++;
                    continue;
                }

                // Inference will call MapDownloadAsync itself once it fills in metadata.
                // If we map here in parallel we race InferAnimationMetadata's MapDownloadAsync
                // and can replace the canonical mapping with a stale /unknown/... one.
                if (!info.IsAiProcessed && info.AiRetryCount < InferenceMaxRetryCount)
                {
                    advancedSkip++;
                    continue;
                }

                if (await fileMappingRepository.ExistsForAnimationInfoAsync(info.Id, cancellationToken))
                {
                    advancedSkip++;
                    continue;
                }

                try
                {
                    await fileMapper.MapDownloadAsync(info.Id, cancellationToken);
                    migrated++;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    failed++;
                    advancedSkip++;
                    LogItemFailed(logger, ex, info.Id);
                }
            }

            // Migrated rows now have mappings and will be filtered by ExistsForAnimationInfoAsync
            // when they appear on the next page. Skipped and failed rows won't, so we advance
            // past them here to avoid retrying failures forever or leapfrogging them later.
            skip += advancedSkip;
            if (skip >= page.TotalCount) break;
        }

        LogSummary(logger, migrated, failed);
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to migrate file mappings for AnimationInfo {ItemId}")]
    private static partial void LogItemFailed(ILogger logger, Exception ex, Guid itemId);

    [LoggerMessage(Level = LogLevel.Information, Message = "File mapping migration: {Migrated} migrated, {Failed} failed")]
    private static partial void LogSummary(ILogger logger, int migrated, int failed);
}
