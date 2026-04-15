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
        var fileStoreProvider = scope.ServiceProvider.GetRequiredService<IFileStoreProvider>();

        var info = await animationInfoRepository.FindByIdWithAnimationAsync(param.ItemId, cancellationToken);
        if (info?.Animation is null || info.StorePath is null)
        {
            LogRenameSkipped(logger, param.ItemId, info?.Animation is null, info?.StorePath is null);
            return;
        }

        var fileStore = fileStoreProvider.GetRequiredClient(info.FileStore!);

        LogStartingFileRename(logger, param.ItemId, info.Animation.Name, info.Season, info.Episode, info.StorePath);

        try
        {
            var context = new FileRenameContext(
                AnimationName: info.Animation.Name,
                Season: info.Season ?? 1,
                Episode: info.Episode,
                OriginalTitle: info.Title,
                StorePath: info.StorePath);

            await fileRenamer.RenameAsync(context, cancellationToken);
            LogFileRenameCompleted(logger, param.ItemId);

            // If StorePath is a single file (not a directory), update it to the new path after rename
            var fileInfo = await fileStore.FileInfoAsync(info.StorePath, cancellationToken);
            if (!fileInfo.IsDirectory && info.Season is not null && info.Episode is not null)
            {
                var dir = Path.GetDirectoryName(fileInfo.Path)!;
                var ext = Path.GetExtension(fileInfo.Path);
                var newName = $"{SanitizeFileName(info.Animation.Name)} S{info.Season:D2}E{info.Episode:D2}{ext}";
                var newPath = Path.Combine(dir, newName);

                if (File.Exists(newPath))
                {
                    var updatedInfo = info with { StorePath = newPath };
                    await animationInfoRepository.UpdateAsync(updatedInfo, cancellationToken);
                    LogStorePathUpdated(logger, param.ItemId, newPath);
                }
                else
                {
                    LogStorePathUpdateSkipped(logger, param.ItemId, newPath);
                }
            }
        }
        catch (Exception ex)
        {
            LogFileRenameFailed(logger, ex, param.ItemId);
        }
    }

    private static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return string.Concat(name.Select(c => invalid.Contains(c) ? '_' : c));
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Starting file rename for {ItemId}: animation={AnimationName}, S{Season}E{Episode}, storePath={StorePath}")]
    private static partial void LogStartingFileRename(ILogger logger, Guid itemId, string animationName, int? season, int? episode, string storePath);

    [LoggerMessage(Level = LogLevel.Debug, Message = "File rename completed for {ItemId}")]
    private static partial void LogFileRenameCompleted(ILogger logger, Guid itemId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "File rename failed for {ItemId}")]
    private static partial void LogFileRenameFailed(ILogger logger, Exception ex, Guid itemId);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Rename skipped for {ItemId}: animationNull={AnimationNull}, storePathNull={StorePathNull}")]
    private static partial void LogRenameSkipped(ILogger logger, Guid itemId, bool animationNull, bool storePathNull);

    [LoggerMessage(Level = LogLevel.Information, Message = "Updated StorePath for {ItemId} after rename: {NewPath}")]
    private static partial void LogStorePathUpdated(ILogger logger, Guid itemId, string newPath);

    [LoggerMessage(Level = LogLevel.Debug, Message = "StorePath update skipped for {ItemId}: new path does not exist: {NewPath}")]
    private static partial void LogStorePathUpdateSkipped(ILogger logger, Guid itemId, string newPath);
}
