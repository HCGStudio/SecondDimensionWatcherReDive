using System.Diagnostics;
using SecondDimensionWatcherReDive.Framework.Tasks;
using SecondDimensionWatcherReDive.Observability;

namespace SecondDimensionWatcherReDive.Services;

public partial class ScheduledTaskBackgroundService<TTask>(
    TTask task,
    IScheduledTaskLeaseManager leaseManager,
    RuntimeTelemetry telemetry,
    ILogger<ScheduledTaskBackgroundService<TTask>> logger) : BackgroundService
    where TTask : ScheduledTaskBase
{
    private static readonly TimeSpan ContendedLeasePollInterval = TimeSpan.FromSeconds(10);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        LogStartingScheduledTask(logger, task.Id);

        await Task.WhenAll(
            task.ProcessQueueAsync(leaseManager, stoppingToken),
            RunTimerLoopAsync(stoppingToken));
    }

    private async Task RunTimerLoopAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var delay = task.Interval;
            if (task.IsEnabled)
            {
                var startedAt = Stopwatch.GetTimestamp();
                try
                {
                    var executed = await task.RunScheduledAsync(stoppingToken);
                    if (!executed)
                        delay = ContendedLeasePollInterval;
                    telemetry.RecordScheduledTask(
                        task.Id,
                        executed ? "completed" : "contended",
                        Stopwatch.GetElapsedTime(startedAt));
                }
                catch (ScheduledTaskLeaseUnavailableException ex)
                {
                    delay = ContendedLeasePollInterval;
                    LogScheduledTaskLeaseUnavailable(logger, ex, task.Id);
                    telemetry.RecordScheduledTask(
                        task.Id,
                        "lease_unavailable",
                        Stopwatch.GetElapsedTime(startedAt));
                }
                catch (OperationCanceledException ex) when (!stoppingToken.IsCancellationRequested)
                {
                    delay = ContendedLeasePollInterval;
                    LogScheduledTaskLeaseLost(logger, ex, task.Id);
                    telemetry.RecordScheduledTask(
                        task.Id,
                        "lease_lost",
                        Stopwatch.GetElapsedTime(startedAt));
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    LogScheduledTaskFailed(logger, ex, task.Id);
                    telemetry.RecordScheduledTask(
                        task.Id,
                        "failed",
                        Stopwatch.GetElapsedTime(startedAt));
                }
            }

            await Task.Delay(delay, stoppingToken);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Starting scheduled task: {TaskId}")]
    private static partial void LogStartingScheduledTask(ILogger logger, string taskId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Scheduled task {TaskId} failed during timer execution")]
    private static partial void LogScheduledTaskFailed(ILogger logger, Exception ex, string taskId);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Scheduled task {TaskId} lease is unavailable; retrying soon")]
    private static partial void LogScheduledTaskLeaseUnavailable(
        ILogger logger,
        Exception exception,
        string taskId);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Scheduled task {TaskId} execution was cancelled or lost its lease; retrying soon")]
    private static partial void LogScheduledTaskLeaseLost(
        ILogger logger,
        Exception exception,
        string taskId);
}
