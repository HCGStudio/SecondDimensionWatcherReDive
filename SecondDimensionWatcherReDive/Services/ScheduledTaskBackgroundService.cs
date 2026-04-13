using SecondDimensionWatcherReDive.Framework.Tasks;

namespace SecondDimensionWatcherReDive.Services;

public partial class ScheduledTaskBackgroundService<TTask>(
    TTask task,
    ILogger<ScheduledTaskBackgroundService<TTask>> logger) : BackgroundService
    where TTask : ScheduledTaskBase
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        LogStartingScheduledTask(logger, task.Id);

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
                    LogScheduledTaskFailed(logger, ex, task.Id);
                }
            }

            await Task.Delay(task.Interval, stoppingToken);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Starting scheduled task: {TaskId}")]
    private static partial void LogStartingScheduledTask(ILogger logger, string taskId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Scheduled task {TaskId} failed during timer execution")]
    private static partial void LogScheduledTaskFailed(ILogger logger, Exception ex, string taskId);
}
