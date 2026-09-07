using SecondDimensionWatcherReDive.Framework.DataRepository;

namespace SecondDimensionWatcherReDive.Observability;

internal sealed partial class DurableJobMetricsBackgroundService(
    IServiceScopeFactory scopeFactory,
    RuntimeTelemetry telemetry,
    ILogger<DurableJobMetricsBackgroundService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(15));
        do
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var repository = scope.ServiceProvider.GetRequiredService<IDurableJobRepository>();
                telemetry.UpdateJobStatistics(await repository.GetStatisticsAsync(
                    DateTimeOffset.UtcNow,
                    stoppingToken));
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                LogCollectionFailed(logger, exception);
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    [LoggerMessage(Level = LogLevel.Debug,
        Message = "Durable job metric collection failed")]
    private static partial void LogCollectionFailed(ILogger logger, Exception exception);
}
