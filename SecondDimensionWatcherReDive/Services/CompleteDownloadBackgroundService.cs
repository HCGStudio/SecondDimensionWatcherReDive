using System.Threading.Channels;
using SecondDimensionWatcherReDive.Data;
using SecondDimensionWatcherReDive.Framework.PluginParams;
using SecondDimensionWatcherReDive.Framework.DataRepository;
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
            await eventTrigger.InvokeAsync(new FileDownloadCompleteParam(
                request.ItemId, request.StorePath, request.FileStore), cancellationToken);
            LogPluginEventCompleted(logger, request.ItemId);
        }
        catch (Exception ex)
        {
            LogDownloadCompletedEventFailed(logger, ex, request.ItemId);
        }
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
}
