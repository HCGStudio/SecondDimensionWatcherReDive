using System.Threading.Channels;
using SecondDimensionWatcherReDive.Data;
using SecondDimensionWatcherReDive.Framework.FileStore;
using SecondDimensionWatcherReDive.Framework.PluginParams;
using SecondDimensionWatcherReDive.Framework.DataRepository;
using SecondDimensionWatcherReDive.Plugin;
using SecondDimensionWatcherReDive.Utils.FileStore;

namespace SecondDimensionWatcherReDive.Services;

public partial class CompleteDownloadBackgroundService(
    Channel<DownloadCompleteRequest> downloadCompleteRequest,
    IServiceScopeFactory scopeFactory,
    ILogger<CompleteDownloadBackgroundService> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        var reader = downloadCompleteRequest.Reader;
        while (await reader.WaitToReadAsync(cancellationToken))
        {
            var request = await reader.ReadAsync(cancellationToken);
            await ProcessRequest(request, cancellationToken);
        }
    }

    private async Task ProcessRequest(DownloadCompleteRequest request, CancellationToken cancellationToken)
    {
        LogProcessingRequest(logger, request.ItemId, request.StorePath, request.FileStore);

        await using var scope = scopeFactory.CreateAsyncScope();
        var animationInfoRepository = scope.ServiceProvider.GetRequiredService<IAnimationInfoRepository>();

        var info = await animationInfoRepository.FindByIdWithAnimationAsync(request.ItemId, cancellationToken);

        if (info is null)
        {
            LogAnimationInfoNotFound(logger, request.ItemId);
            return;
        }

        info = info with
        {
            IsDownloadFinished = true,
            DownloadEndTime = DateTimeOffset.Now,
            FileStore = request.FileStore,
            StorePath = request.StorePath
        };

        await animationInfoRepository.UpdateAsync(info, cancellationToken);
        LogDownloadMarkedFinished(logger, request.ItemId, info.Title);

        // Fire plugin event
        try
        {
            LogFiringPluginEvent(logger, request.ItemId);
            var eventTrigger = scope.ServiceProvider
                .GetRequiredService<IPluginEventTrigger<FileDownloadCompleteParam>>();
            await eventTrigger.Invoke(new FileDownloadCompleteParam(
                request.ItemId, request.StorePath, request.FileStore));
            LogPluginEventCompleted(logger, request.ItemId);
        }
        catch (Exception ex)
        {
            LogDownloadCompletedEventFailed(logger, ex, request.ItemId);
        }

        // Rename files after download completes
        var fileRenamer = scope.ServiceProvider.GetRequiredService<IFileRenamer>();
        var fileStoreProvider = scope.ServiceProvider.GetRequiredService<IFileStoreProvider>();
        var fileStore = fileStoreProvider.GetRequiredClient(info.FileStore!);

        if (info.Animation != null && info.StorePath != null)
        {
            LogStartingFileRename(logger, request.ItemId, info.Animation.Name, info.Season, info.Episode, info.StorePath);
            try
            {
                var context = new FileRenameContext(
                    AnimationName: info.Animation.Name,
                    Season: info.Season ?? 1,
                    Episode: info.Episode,
                    OriginalTitle: info.Title,
                    StorePath: info.StorePath);

                await fileRenamer.RenameAsync(context, cancellationToken);
                LogFileRenameCompleted(logger, request.ItemId);

                // If StorePath is a single file (not a directory), update it to the new path after rename
                var fileInfo = await fileStore.FileInfo(info.StorePath);
                if (!fileInfo.IsDirectory && info.Season is not null && info.Episode is not null)
                {
                    var dir = Path.GetDirectoryName(fileInfo.Path)!;
                    var ext = Path.GetExtension(fileInfo.Path);
                    var newName = $"{SanitizeFileName(info.Animation.Name)} S{info.Season:D2}E{info.Episode:D2}{ext}";
                    var newPath = Path.Combine(dir, newName);

                    if (File.Exists(newPath))
                    {
                        info = info with { StorePath = newPath };
                        await animationInfoRepository.UpdateAsync(info, cancellationToken);
                        LogStorePathUpdated(logger, request.ItemId, newPath);
                    }
                    else
                    {
                        LogStorePathUpdateSkipped(logger, request.ItemId, newPath);
                    }
                }
            }
            catch (Exception ex)
            {
                LogFileRenameFailed(logger, ex, request.ItemId);
            }
        }
        else
        {
            LogRenameSkipped(logger, request.ItemId, info.Animation is null, info.StorePath is null);
        }
    }

    private static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return string.Concat(name.Select(c => invalid.Contains(c) ? '_' : c));
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Processing download complete request for {ItemId}, storePath: {StorePath}, fileStore: {FileStore}")]
    private static partial void LogProcessingRequest(ILogger logger, Guid itemId, string storePath, string fileStore);

    [LoggerMessage(Level = LogLevel.Warning, Message = "AnimationInfo not found for {ItemId}, skipping")]
    private static partial void LogAnimationInfoNotFound(ILogger logger, Guid itemId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Download marked finished for {ItemId}: {Title}")]
    private static partial void LogDownloadMarkedFinished(ILogger logger, Guid itemId, string title);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Firing OnFileDownloadCompleted plugin event for {ItemId}")]
    private static partial void LogFiringPluginEvent(ILogger logger, Guid itemId);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Plugin event completed for {ItemId}")]
    private static partial void LogPluginEventCompleted(ILogger logger, Guid itemId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "OnFileDownloadCompleted event failed for {ItemId}")]
    private static partial void LogDownloadCompletedEventFailed(ILogger logger, Exception ex, Guid itemId);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Starting file rename for {ItemId}: animation={AnimationName}, S{Season}E{Episode}, storePath={StorePath}")]
    private static partial void LogStartingFileRename(ILogger logger, Guid itemId, string animationName, int? season, int? episode, string storePath);

    [LoggerMessage(Level = LogLevel.Debug, Message = "File rename completed for {ItemId}")]
    private static partial void LogFileRenameCompleted(ILogger logger, Guid itemId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "File rename failed for {ItemId}")]
    private static partial void LogFileRenameFailed(ILogger logger, Exception ex, Guid itemId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Updated StorePath for {ItemId} after rename: {NewPath}")]
    private static partial void LogStorePathUpdated(ILogger logger, Guid itemId, string newPath);

    [LoggerMessage(Level = LogLevel.Debug, Message = "StorePath update skipped for {ItemId}: new path does not exist: {NewPath}")]
    private static partial void LogStorePathUpdateSkipped(ILogger logger, Guid itemId, string newPath);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Rename skipped for {ItemId}: animationNull={AnimationNull}, storePathNull={StorePathNull}")]
    private static partial void LogRenameSkipped(ILogger logger, Guid itemId, bool animationNull, bool storePathNull);
}
