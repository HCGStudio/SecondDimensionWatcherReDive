using SecondDimensionWatcherReDive.Framework.Tasks;

namespace SecondDimensionWatcherReDive.Services;

public class ScheduledTaskBackgroundService<TTask>(
    TTask task,
    ILogger<ScheduledTaskBackgroundService<TTask>> logger) : BackgroundService
    where TTask : ScheduledTaskBase
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Starting scheduled task: {TaskId}", task.Id);

        await Task.WhenAll(
            task.ProcessQueueAsync(stoppingToken),
            RunTimerLoopAsync(stoppingToken));
    }

    private async Task RunTimerLoopAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            if (task.IsEnabled)
            {
                try
                {
                    await task.RunNowAsync(stoppingToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogError(ex, "Scheduled task {TaskId} failed during timer execution", task.Id);
                }
            }

            await Task.Delay(task.Interval, stoppingToken);
        }
    }
}
