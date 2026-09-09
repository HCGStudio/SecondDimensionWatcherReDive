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
                // Individual remote calls are bounded by the service. A shared
                // deadline would repeatedly roll back large, otherwise healthy scans.
                more = await scope.ServiceProvider.GetRequiredService<DownloadCapacityService>().ProcessNextAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception exception) { logger.LogWarning(exception, "Download capacity reconciliation will retry"); }
            await Task.Delay(more ? TimeSpan.FromMilliseconds(100) : TimeSpan.FromSeconds(10), stoppingToken);
        }
    }
}
