using System.Threading.Channels;
using Microsoft.EntityFrameworkCore;
using SecondDimensionWatcherReDive.Data;
using SecondDimensionWatcherReDive.Framework.FileStore;
using SecondDimensionWatcherReDive.Framework.PluginParams;
using SecondDimensionWatcherReDive.Models;
using SecondDimensionWatcherReDive.Plugin;

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
        await using var scope = scopeFactory.CreateAsyncScope();
        await using var applicationContext = scope.ServiceProvider.GetRequiredService<ApplicationContext>();

        var info = await applicationContext.AnimationInfo
            .Include(a => a.Animation)
            .FirstOrDefaultAsync(a => a.Id == request.ItemId, cancellationToken);

        if (info is null)
            return;

        info.IsDownloadFinished = true;
        info.DownloadEndTime = DateTimeOffset.Now;
        info.FileStore = request.FileStore;
        info.StorePath = request.StorePath;

        await applicationContext.SaveChangesAsync(cancellationToken);

        // Fire plugin event
        try
        {
            var eventTrigger = scope.ServiceProvider
                .GetRequiredService<IPluginEventTrigger<FileDownloadCompleteParam>>();
            await eventTrigger.Invoke(new FileDownloadCompleteParam(
                request.ItemId, request.StorePath, request.FileStore));
        }
        catch (Exception ex)
        {
            LogDownloadCompletedEventFailed(logger, ex, request.ItemId);
        }

        // Rename files after download completes
        var fileRenamer = scope.ServiceProvider.GetService<IFileRenamer>();
        if (fileRenamer != null && info.Animation != null && info.StorePath != null)
        {
            try
            {
                var context = new FileRenameContext(
                    AnimationName: info.Animation.Name,
                    Season: info.Season ?? 1,
                    Episode: info.Episode,
                    OriginalTitle: info.Title,
                    StorePath: info.StorePath);

                await fileRenamer.RenameAsync(context, cancellationToken);

                // If StorePath is a single file (not a directory), update it to the new path after rename
                if (info.Episode != null && !Directory.Exists(info.StorePath) && File.Exists(info.StorePath) is false)
                {
                    var dir = Path.GetDirectoryName(info.StorePath)!;
                    var ext = Path.GetExtension(info.StorePath);
                    var season = info.Season ?? 1;
                    var newName = $"{SanitizeFileName(info.Animation.Name)} S{season:D2}E{info.Episode:D2}{ext}";
                    var newPath = Path.Combine(dir, newName);

                    if (File.Exists(newPath))
                    {
                        info.StorePath = newPath;
                        await applicationContext.SaveChangesAsync(cancellationToken);
                        LogStorePathUpdated(logger, request.ItemId, newPath);
                    }
                }
            }
            catch (Exception ex)
            {
                LogFileRenameFailed(logger, ex, request.ItemId);
            }
        }
    }

    private static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return string.Concat(name.Select(c => invalid.Contains(c) ? '_' : c));
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "OnFileDownloadCompleted event failed for {ItemId}")]
    private static partial void LogDownloadCompletedEventFailed(ILogger logger, Exception ex, Guid itemId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "File rename failed for {ItemId}")]
    private static partial void LogFileRenameFailed(ILogger logger, Exception ex, Guid itemId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Updated StorePath for {ItemId} after rename: {NewPath}")]
    private static partial void LogStorePathUpdated(ILogger logger, Guid itemId, string newPath);
}