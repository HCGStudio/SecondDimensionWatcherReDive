using SecondDimensionWatcherReDive.Utils.FileDownload;

namespace SecondDimensionWatcherReDive.Services;

public sealed class DownloadCapacityBackgroundService(
    IServiceScopeFactory scopeFactory, ILogger<DownloadCapacityBackgroundService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var more = false;
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                timeout.CancelAfter(TimeSpan.FromSeconds(30));
                more = await scope.ServiceProvider.GetRequiredService<DownloadCapacityService>().ProcessNextAsync(timeout.Token);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception exception) { logger.LogWarning(exception, "Download capacity reconciliation will retry"); }
            await Task.Delay(more ? TimeSpan.FromMilliseconds(100) : TimeSpan.FromSeconds(10), stoppingToken);
        }
    }
}
