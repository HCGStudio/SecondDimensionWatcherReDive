using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SecondDimensionWatcherReDive.Framework.DataRepository;
using SecondDimensionWatcherReDive.Framework.FileStore;
using SecondDimensionWatcherReDive.Framework.Plugin;
using SecondDimensionWatcherReDive.Framework.PluginParams;

namespace SecondDimensionWatcherReDive.Plugin.FileRenamer;

public partial class FileRenamerPlugin(IServiceScopeFactory scopeFactory, ILogger<FileRenamerPlugin> logger)
    : PluginBase
{
    public override IPluginInfo Info { get; } = new PluginInfo(
        "FileRenamer",
        "Renames downloaded video files to standardized S##E## format after download completes",
        "MIT",
        "");

    protected override async Task OnDownloadCompleted(FileDownloadCompleteParam param, CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();

        var animationInfoRepository = scope.ServiceProvider.GetRequiredService<IAnimationInfoRepository>();
        var fileRenamer = scope.ServiceProvider.GetRequiredService<IFileRenamer>();

        var info = await animationInfoRepository.FindByIdWithAnimationAsync(param.ItemId, cancellationToken);
        if (info?.Animation is null || info.StorePath is null)
        {
            LogRenameSkipped(logger, param.ItemId, info?.Animation is null, info?.StorePath is null);
            return;
        }

        LogStartingFileRename(logger, param.ItemId, info.Animation.Name, info.Season, info.Episode, info.StorePath);

        try
        {
            if (info.Episode is not null)
            {
                var request = new FileRenameRequest(
                    AnimationName: info.Animation.Name,
                    Season: info.Season ?? 1,
                    Episode: info.Episode.Value,
                    StorePath: info.StorePath,
                    AnimationInfo: info);

                await fileRenamer.RenameAsync(request, cancellationToken);
            }
            else
            {
                var request = new MultipleFileRenameRequest(
                    AnimationName: info.Animation.Name,
                    Season: info.Season ?? 1,
                    OriginalTitle: info.Title,
                    Path: info.StorePath);

                await fileRenamer.RenameMultipleAsync(request, cancellationToken);
            }

            LogFileRenameCompleted(logger, param.ItemId);
        }
        catch (Exception ex)
        {
            LogFileRenameFailed(logger, ex, param.ItemId);
        }
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Starting file rename for {ItemId}: animation={AnimationName}, S{Season}E{Episode}, storePath={StorePath}")]
    private static partial void LogStartingFileRename(ILogger logger, Guid itemId, string animationName, int? season, int? episode, string storePath);

    [LoggerMessage(Level = LogLevel.Debug, Message = "File rename completed for {ItemId}")]
    private static partial void LogFileRenameCompleted(ILogger logger, Guid itemId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "File rename failed for {ItemId}")]
    private static partial void LogFileRenameFailed(ILogger logger, Exception ex, Guid itemId);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Rename skipped for {ItemId}: animationNull={AnimationNull}, storePathNull={StorePathNull}")]
    private static partial void LogRenameSkipped(ILogger logger, Guid itemId, bool animationNull, bool storePathNull);
}
