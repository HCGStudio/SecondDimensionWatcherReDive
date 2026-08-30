using System.Diagnostics;
using System.Text.Json;
using System.Threading.Channels;
using SecondDimensionWatcherReDive.Data;
using SecondDimensionWatcherReDive.Framework.DataRepository;
using SecondDimensionWatcherReDive.Framework.Notifications;
using SecondDimensionWatcherReDive.Framework.PluginParams;
using SecondDimensionWatcherReDive.Plugin;
using SecondDimensionWatcherReDive.Observability;
using SecondDimensionWatcherReDive.Utils.FileStore;
using SecondDimensionWatcherReDive.Utils.Incidents;

namespace SecondDimensionWatcherReDive.Services;

/// <summary>
/// Executes persisted download-completion effects. The channel is deliberately
/// only a wake-up hint: polling and expired leases make work recoverable after a
/// process crash or a lost hint.
/// </summary>
public partial class CompleteDownloadBackgroundService(
    Channel<DownloadCompleteRequest> downloadCompleteRequest,
    IServiceScopeFactory scopeFactory,
    ILogger<CompleteDownloadBackgroundService> logger,
    IIncidentReporter? incidentReporter = null,
    RuntimeTelemetry? telemetry = null)
    : BackgroundService
{
    internal const int MaxAttempts = 8;
    internal static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(10);
    internal static readonly TimeSpan LeaseDuration = TimeSpan.FromMinutes(2);
    internal static readonly TimeSpan LeaseRenewInterval = TimeSpan.FromSeconds(30);

    private readonly string _workerId = $"{Environment.MachineName}:{Environment.ProcessId}:{Guid.NewGuid():N}";

    protected override async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var processed = 0;
            try
            {
                processed = await ProcessDueJobsAsync(cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                // A temporary database outage must not terminate the hosted service.
                LogPollFailed(logger, exception);
            }

            if (processed > 0)
                continue;

            await WaitForWakeOrPollAsync(cancellationToken);
        }
    }

    internal async Task<int> ProcessDueJobsAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var repository = scope.ServiceProvider.GetRequiredService<IDurableJobRepository>();
        var now = DateTimeOffset.UtcNow;
        var jobs = await repository.ClaimDueAsync(
            _workerId,
            now,
            now + LeaseDuration,
            1,
            cancellationToken);

        foreach (var job in jobs)
            await ProcessClaimedJobAsync(scope.ServiceProvider, repository, job, cancellationToken);

        return jobs.Count;
    }

    internal async Task ProcessClaimedJobAsync(
        IServiceProvider serviceProvider,
        IDurableJobRepository repository,
        DurableJob job,
        CancellationToken cancellationToken)
    {
        var startedAt = Stopwatch.GetTimestamp();
        var currentStage = job.Stage;
        using var activity = RuntimeTelemetry.StartDurableJob(job);
        using var renewalCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken);
        using var leaseLost = new CancellationTokenSource();
        using var effectCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            leaseLost.Token);
        var renewalTask = RenewJobLeaseAsync(
            job.Id,
            leaseLost,
            renewalCancellation.Token);
        Guid? itemId = null;
        try
        {
            if (job.Type != DurableJobType.DownloadCompletion)
                throw new NotSupportedException($"Unsupported durable job type: {job.Type}");

            var payload = JsonSerializer.Deserialize<DownloadCompletionJobPayload>(job.PayloadJson)
                          ?? throw new JsonException("The durable job payload is empty.");
            itemId = payload.ItemId;
            var stage = job.Stage;
            currentStage = stage;

            if (stage == DurableJobStage.MapFiles)
            {
                var mapper = serviceProvider.GetRequiredService<IFileMapper>();
                if (!await mapper.MapDownloadAsync(payload.ItemId, effectCancellation.Token))
                    throw new InvalidOperationException("No file mapping could be produced.");

                if (incidentReporter is not null)
                    await incidentReporter.ResolveAsync(
                        IncidentType.FileMappingFailure,
                        payload.ItemId.ToString(),
                        effectCancellation.Token);

                await AdvanceAsync(
                    repository, job.Id, stage, DurableJobStage.Notify, effectCancellation.Token);
                stage = DurableJobStage.Notify;
                currentStage = stage;
            }

            if (stage == DurableJobStage.Notify)
            {
                var notifier = serviceProvider.GetRequiredService<IDownloadCompletionNotifier>();
                await notifier.NotifyAsync(job.Id, payload, effectCancellation.Token);
                await AdvanceAsync(
                    repository, job.Id, stage, DurableJobStage.InvokePlugins, effectCancellation.Token);
                stage = DurableJobStage.InvokePlugins;
                currentStage = stage;
            }

            if (stage == DurableJobStage.InvokePlugins)
            {
                var eventTrigger = serviceProvider
                    .GetRequiredService<IPluginEventTrigger<FileDownloadCompleteParam>>();
                await eventTrigger.InvokeAsync(
                    new FileDownloadCompleteParam(
                        payload.ItemId,
                        payload.StorePath,
                        payload.FileStore,
                        job.Id),
                    effectCancellation.Token);
                await AdvanceAsync(
                    repository, job.Id, stage, DurableJobStage.Done, effectCancellation.Token);
            }

            LogJobCompleted(logger, job.Id, payload.ItemId);
            activity?.SetStatus(ActivityStatusCode.Ok);
            telemetry?.RecordJobAttempt(
                job.Type,
                currentStage,
                "completed",
                Stopwatch.GetElapsedTime(startedAt));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException) when (leaseLost.IsCancellationRequested)
        {
            // Another worker may already own the expired lease. Do not mutate the
            // job with stale ownership; its persisted stage remains resumable.
            LogJobLeaseLost(logger, job.Id);
        }
        catch (Exception exception)
        {
            var attemptCount = job.AttemptCount + 1;
            var attemptedAt = DateTimeOffset.UtcNow;
            var retryAt = attemptCount >= MaxAttempts
                ? (DateTimeOffset?)null
                : attemptedAt + RetryDelay(attemptCount);
            var error = LimitError(exception);

            await repository.MarkFailedAsync(
                job.Id,
                _workerId,
                attemptCount,
                attemptedAt,
                retryAt,
                error,
                cancellationToken);

            if (currentStage == DurableJobStage.MapFiles && incidentReporter is not null)
            {
                await incidentReporter.ReportAsync(new IncidentReport(
                        IncidentType.FileMappingFailure,
                        IncidentSeverity.Error,
                        "Downloaded files could not be mapped",
                        error,
                        (itemId ?? job.Id).ToString()),
                    cancellationToken);
            }

            if (retryAt.HasValue)
                LogJobRetry(logger, exception, job.Id, attemptCount, retryAt.Value);
            else
                LogJobDeadLettered(logger, exception, job.Id, attemptCount);
            activity?.SetStatus(ActivityStatusCode.Error, exception.GetType().Name);
            telemetry?.RecordJobAttempt(
                job.Type,
                currentStage,
                retryAt.HasValue ? "retry" : "dead_letter",
                Stopwatch.GetElapsedTime(startedAt));
        }
        finally
        {
            await renewalCancellation.CancelAsync();
            await renewalTask;
        }
    }

    private async Task RenewJobLeaseAsync(
        Guid jobId,
        CancellationTokenSource leaseLost,
        CancellationToken cancellationToken)
    {
        try
        {
            using var timer = new PeriodicTimer(LeaseRenewInterval);
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                var now = DateTimeOffset.UtcNow;
                await using var scope = scopeFactory.CreateAsyncScope();
                var repository = scope.ServiceProvider.GetRequiredService<IDurableJobRepository>();
                if (await repository.RenewLeaseAsync(
                        jobId,
                        _workerId,
                        now,
                        now + LeaseDuration,
                        cancellationToken))
                    continue;

                leaseLost.Cancel();
                return;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            LogJobLeaseRenewalFailed(logger, exception, jobId);
            leaseLost.Cancel();
        }
    }

    private async Task AdvanceAsync(
        IDurableJobRepository repository,
        Guid jobId,
        DurableJobStage expectedStage,
        DurableJobStage nextStage,
        CancellationToken cancellationToken)
    {
        var advanced = await repository.AdvanceStageAsync(
            jobId,
            _workerId,
            expectedStage,
            nextStage,
            DateTimeOffset.UtcNow,
            cancellationToken);
        if (!advanced)
            throw new InvalidOperationException("The durable job lease was lost.");
    }

    private async Task WaitForWakeOrPollAsync(CancellationToken cancellationToken)
    {
        var reader = downloadCompleteRequest.Reader;
        using var waitCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken);
        waitCancellation.CancelAfter(PollInterval);
        try
        {
            await reader.WaitToReadAsync(waitCancellation.Token);
        }
        catch (OperationCanceledException) when (
            !cancellationToken.IsCancellationRequested
            && waitCancellation.IsCancellationRequested)
        {
            // The bounded timeout is the polling signal. Cancelling the channel
            // wait prevents an abandoned waiter from accumulating every cycle.
        }

        // Coalesce all hints. Their payload is intentionally not processed here;
        // every authoritative request is already persisted transactionally.
        while (reader.TryRead(out _))
        {
        }
    }

    internal static TimeSpan RetryDelay(int attemptCount)
    {
        var seconds = Math.Min(900, 5 * Math.Pow(2, Math.Max(0, attemptCount - 1)));
        return TimeSpan.FromSeconds(seconds);
    }

    private static string LimitError(Exception exception)
    {
        var value = $"{exception.GetType().Name}: {exception.Message}";
        return value.Length <= 512 ? value : value[..512];
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Durable completion polling failed; it will retry")]
    private static partial void LogPollFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Durable completion job {JobId} completed for {ItemId}")]
    private static partial void LogJobCompleted(ILogger logger, Guid jobId, Guid itemId);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Durable completion job {JobId} failed on attempt {Attempt}; retrying at {RetryAt}")]
    private static partial void LogJobRetry(
        ILogger logger,
        Exception exception,
        Guid jobId,
        int attempt,
        DateTimeOffset retryAt);

    [LoggerMessage(Level = LogLevel.Error,
        Message = "Durable completion job {JobId} entered dead-letter after {Attempt} attempts")]
    private static partial void LogJobDeadLettered(
        ILogger logger,
        Exception exception,
        Guid jobId,
        int attempt);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Durable completion job {JobId} lost its lease; another worker will resume it")]
    private static partial void LogJobLeaseLost(ILogger logger, Guid jobId);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Durable completion job {JobId} lease renewal failed")]
    private static partial void LogJobLeaseRenewalFailed(
        ILogger logger,
        Exception exception,
        Guid jobId);
}
