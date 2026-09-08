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
using SecondDimensionWatcherReDive.Utils.ReleaseUpgrades;

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
    RuntimeTelemetry? telemetry = null,
    IConfiguration? configuration = null)
    : BackgroundService
{
    internal const int MaxAttempts = 8;
    internal static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(10);
    internal static readonly TimeSpan LeaseDuration = TimeSpan.FromMinutes(2);
    internal static readonly TimeSpan LeaseRenewInterval = TimeSpan.FromSeconds(30);
    internal static readonly TimeSpan PluginDeferralDelay = TimeSpan.FromSeconds(3);

    private readonly string _workerId = $"{Environment.MachineName}:{Environment.ProcessId}:{Guid.NewGuid():N}";
    // Existing plugins need not opt into cross-task concurrency. Mapping and
    // notifications can proceed while this instance serializes plugin callbacks.
    private readonly SemaphoreSlim _pluginGate = new(1, 1);

    protected override Task ExecuteAsync(CancellationToken cancellationToken) =>
        Task.WhenAll(Enumerable.Range(0, Math.Clamp(configuration?.GetValue<int?>("DownloadCompletion:Workers") ?? 2, 1, 16))
            .Select(index => RunWorkerAsync($"{_workerId}:{index}", cancellationToken)));

    private async Task RunWorkerAsync(string workerId, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var processed = 0;
            try
            {
                processed = await ProcessDueJobsAsync(cancellationToken, workerId);
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

    internal async Task<int> ProcessDueJobsAsync(CancellationToken cancellationToken, string? workerId = null)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var repository = scope.ServiceProvider.GetRequiredService<IDurableJobRepository>();
        var now = DateTimeOffset.UtcNow;
        var claimWorkerId = workerId ?? _workerId;
        var jobs = await repository.ClaimDueAsync(
            claimWorkerId,
            now,
            now + LeaseDuration,
            1,
            cancellationToken);

        foreach (var job in jobs)
        {
            // The returned row may already have expired in transit. Never adopt
            // an owner from the row as this worker's identity.
            if (job.LeaseOwner != claimWorkerId || job.Status != DurableJobStatus.Processing
                || job.LeaseExpiresAt is null || job.LeaseExpiresAt <= DateTimeOffset.UtcNow)
                continue;
            await ProcessClaimedJobAsync(scope.ServiceProvider, repository, job, cancellationToken, claimWorkerId);
        }

        return jobs.Count;
    }

    internal async Task ProcessClaimedJobAsync(
        IServiceProvider serviceProvider,
        IDurableJobRepository repository,
        DurableJob job,
        CancellationToken cancellationToken,
        string? claimedWorkerId = null)
    {
        var startedAt = Stopwatch.GetTimestamp();
        var workerId = claimedWorkerId ?? _workerId;
        telemetry?.RecordJobQueueWait(job.Type, DateTimeOffset.UtcNow - job.NextAttemptAt);
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
            workerId,
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

                if (serviceProvider.GetService<IReleaseUpgradeCoordinator>() is { } upgradeCoordinator)
                    await upgradeCoordinator.TryActivateCandidateAsync(payload.ItemId, effectCancellation.Token);

                if (incidentReporter is not null)
                    await incidentReporter.ResolveAsync(
                        IncidentType.FileMappingFailure,
                        payload.ItemId.ToString(),
                        effectCancellation.Token);

                await AdvanceAsync(
                    repository, job.Id, workerId, stage, DurableJobStage.Notify, effectCancellation.Token);
                stage = DurableJobStage.Notify;
                currentStage = stage;
            }

            if (stage == DurableJobStage.Notify)
            {
                var notifier = serviceProvider.GetRequiredService<IDownloadCompletionNotifier>();
                await notifier.NotifyAsync(job.Id, payload, effectCancellation.Token);
                await AdvanceAsync(
                    repository, job.Id, workerId, stage, DurableJobStage.InvokePlugins, effectCancellation.Token);
                stage = DurableJobStage.InvokePlugins;
                currentStage = stage;
            }

            if (stage == DurableJobStage.InvokePlugins)
            {
                if (!await _pluginGate.WaitAsync(0, effectCancellation.Token))
                {
                    var deferredAt = DateTimeOffset.UtcNow;
                    if (await repository.DeferAsync(job.Id, workerId, stage,
                            deferredAt, deferredAt + PluginDeferralDelay, effectCancellation.Token))
                    {
                        activity?.SetTag("job.deferred", true);
                        activity?.SetStatus(ActivityStatusCode.Ok);
                        telemetry?.RecordJobDeferred(job.Type, stage);
                        // Wake idle workers so they recompute their wait against
                        // the newly persisted retry time.
                        downloadCompleteRequest.Writer.TryWrite(new DownloadCompleteRequest(
                            payload.ItemId, payload.StorePath, payload.FileStore, payload.DownloadAttemptId));
                    }
                    else
                    {
                        LogJobLeaseLost(logger, job.Id);
                    }
                    // Preserve InvokePlugins without consuming a failure attempt.
                    // This worker can immediately map/notify another download.
                    return;
                }
                try
                {
                    var eventTrigger = serviceProvider
                        .GetRequiredService<IPluginEventTrigger<FileDownloadCompleteParam>>();
                    await eventTrigger.InvokeAsync(
                        new FileDownloadCompleteParam(payload.ItemId, payload.StorePath, payload.FileStore, job.Id),
                        effectCancellation.Token);
                }
                finally { _pluginGate.Release(); }
                await AdvanceAsync(
                    repository, job.Id, workerId, stage, DurableJobStage.Done, effectCancellation.Token);
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
                workerId,
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
        string workerId,
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
                        workerId,
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
        string workerId,
        DurableJobStage expectedStage,
        DurableJobStage nextStage,
        CancellationToken cancellationToken)
    {
        var advanced = await repository.AdvanceStageAsync(
            jobId,
            workerId,
            expectedStage,
            nextStage,
            DateTimeOffset.UtcNow,
            cancellationToken);
        if (!advanced)
            throw new InvalidOperationException("The durable job lease was lost.");
    }

    private async Task WaitForWakeOrPollAsync(CancellationToken cancellationToken)
    {
        var delay = PollInterval;
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var repository = scope.ServiceProvider.GetRequiredService<IDurableJobRepository>();
            if (await repository.GetNextPendingAttemptAtAsync(cancellationToken) is { } nextAttemptAt)
            {
                var untilDue = nextAttemptAt - DateTimeOffset.UtcNow;
                delay = untilDue <= TimeSpan.Zero ? TimeSpan.Zero :
                    untilDue < PollInterval ? untilDue : PollInterval;
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            // A scheduling lookup failure retains the normal polling fallback.
            LogPollFailed(logger, exception);
        }

        var reader = downloadCompleteRequest.Reader;
        using var waitCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken);
        waitCancellation.CancelAfter(delay);
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
