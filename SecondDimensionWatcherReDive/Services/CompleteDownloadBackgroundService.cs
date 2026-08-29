using System.Threading.Channels;
using SecondDimensionWatcherReDive.Data;
using SecondDimensionWatcherReDive.Framework.PluginParams;
using SecondDimensionWatcherReDive.Framework.DataRepository;
using SecondDimensionWatcherReDive.Plugin;
using SecondDimensionWatcherReDive.Utils.FileStore;
using SecondDimensionWatcherReDive.Utils.Incidents;
using SecondDimensionWatcherReDive.Utils.ReleaseUpgrades;

namespace SecondDimensionWatcherReDive.Services;

public partial class CompleteDownloadBackgroundService(
    Channel<DownloadCompleteRequest> downloadCompleteRequest,
    IServiceScopeFactory scopeFactory,
    ILogger<CompleteDownloadBackgroundService> logger,
    IIncidentReporter? incidentReporter = null)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        var reader = downloadCompleteRequest.Reader;
        while (await reader.WaitToReadAsync(cancellationToken))
        {
            var request = await reader.ReadAsync(cancellationToken);
            for (var attempt = 1; attempt <= 3; attempt++)
            {
                try
                {
                    await ProcessRequestAsync(request, cancellationToken);
                    break;
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex) when (attempt < 3)
                {
                    LogProcessingRequestRetry(logger, ex, request.ItemId, attempt);
                    await Task.Delay(TimeSpan.FromMilliseconds(250 * attempt), cancellationToken);
                }
                catch (Exception ex)
                {
                    LogProcessingRequestFailed(logger, ex, request.ItemId);
                    await ReportCompletionFailureAsync(request, ex, cancellationToken);

                    // The torrent tracker removes finished torrents after handing
                    // them to this queue. Requeue here so a transient database
                    // outage cannot permanently lose the completion transition.
                    await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
                    await downloadCompleteRequest.Writer.WriteAsync(request, cancellationToken);
                    LogProcessingRequestRequeued(logger, request.ItemId);
                }
            }
        }
    }

    internal async Task ProcessRequestAsync(
        DownloadCompleteRequest request,
        CancellationToken cancellationToken)
    {
        LogProcessingRequest(logger, request.ItemId, request.StorePath, request.FileStore);

        await using var scope = scopeFactory.CreateAsyncScope();
        var animationInfoRepository = scope.ServiceProvider.GetRequiredService<IAnimationInfoRepository>();

        var info = await animationInfoRepository.TryCompleteDownloadAsync(
            request.ItemId,
            request.DownloadAttemptId,
            request.FileStore,
            request.StorePath,
            DateTimeOffset.Now,
            cancellationToken);

        if (info is null)
        {
            if (scope.ServiceProvider.GetService<IReleaseUpgradeCoordinator>() is { } retryCoordinator &&
                await retryCoordinator.TryActivateCandidateAsync(request.ItemId, cancellationToken) is not null)
                return;
            LogCompletionIgnored(logger, request.ItemId);
            return;
        }
        LogDownloadMarkedFinished(logger, request.ItemId, info.Title);

        if (incidentReporter is not null)
        {
            await incidentReporter.ResolveAsync(
                IncidentType.DownloadStalled,
                request.ItemId.ToString(),
                cancellationToken);
        }

        // Build virtual-fs mappings for the downloaded files.
        var mappingSucceeded = false;
        try
        {
            var fileMapper = scope.ServiceProvider.GetRequiredService<IFileMapper>();
            if (!await fileMapper.MapDownloadAsync(request.ItemId, cancellationToken))
                throw new InvalidOperationException("No file mapping could be produced.");
            mappingSucceeded = true;

            if (incidentReporter is not null)
            {
                await incidentReporter.ResolveAsync(
                    IncidentType.FileMappingFailure,
                    request.ItemId.ToString(),
                    cancellationToken);
            }
        }
        catch (Exception ex)
        {
            LogFileMappingFailed(logger, ex, request.ItemId);
            if (incidentReporter is not null)
            {
                await incidentReporter.ReportAsync(new IncidentReport(
                        IncidentType.FileMappingFailure,
                        IncidentSeverity.Error,
                        "Downloaded files could not be mapped",
                        ex.Message,
                        request.ItemId.ToString()),
                    cancellationToken);
            }
        }

        if (mappingSucceeded && scope.ServiceProvider.GetService<IReleaseUpgradeCoordinator>() is { } coordinator)
        {
            await coordinator.TryActivateCandidateAsync(request.ItemId, cancellationToken);
        }

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

    private async Task ReportCompletionFailureAsync(
        DownloadCompleteRequest request,
        Exception exception,
        CancellationToken cancellationToken)
    {
        if (incidentReporter is null) return;

        try
        {
            await incidentReporter.ReportAsync(new IncidentReport(
                    IncidentType.DownloadStalled,
                    IncidentSeverity.Error,
                    "Download completion could not be persisted",
                    exception.Message,
                    request.ItemId.ToString()),
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception reportException)
        {
            // Persistence may be unavailable for both the completion and its
            // incident. The queued retry remains the source of eventual recovery.
            LogCompletionIncidentFailed(logger, reportException, request.ItemId);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Processing download complete request for {ItemId}, storePath: {StorePath}, fileStore: {FileStore}")]
    private static partial void LogProcessingRequest(ILogger logger, Guid itemId, string storePath, string fileStore);

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Ignoring completion for {ItemId} because it was cancelled, already completed, or removed")]
    private static partial void LogCompletionIgnored(ILogger logger, Guid itemId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to process completion for {ItemId}")]
    private static partial void LogProcessingRequestFailed(ILogger logger, Exception exception, Guid itemId);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Retrying completion for {ItemId} after attempt {Attempt}")]
    private static partial void LogProcessingRequestRetry(
        ILogger logger,
        Exception exception,
        Guid itemId,
        int attempt);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Requeued failed completion for {ItemId}")]
    private static partial void LogProcessingRequestRequeued(ILogger logger, Guid itemId);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Failed to record completion incident for {ItemId}")]
    private static partial void LogCompletionIncidentFailed(
        ILogger logger,
        Exception exception,
        Guid itemId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Download marked finished for {ItemId}: {Title}")]
    private static partial void LogDownloadMarkedFinished(ILogger logger, Guid itemId, string title);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Firing OnFileDownloadCompleted plugin event for {ItemId}")]
    private static partial void LogFiringPluginEvent(ILogger logger, Guid itemId);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Plugin event completed for {ItemId}")]
    private static partial void LogPluginEventCompleted(ILogger logger, Guid itemId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "OnFileDownloadCompleted event failed for {ItemId}")]
    private static partial void LogDownloadCompletedEventFailed(ILogger logger, Exception ex, Guid itemId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "File mapping failed for {ItemId}")]
    private static partial void LogFileMappingFailed(ILogger logger, Exception ex, Guid itemId);
}
