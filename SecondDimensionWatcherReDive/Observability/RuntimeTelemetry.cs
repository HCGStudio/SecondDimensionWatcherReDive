using System.Diagnostics;
using System.Diagnostics.Metrics;
using SecondDimensionWatcherReDive.Framework.DataRepository;

namespace SecondDimensionWatcherReDive.Observability;

public sealed class RuntimeTelemetry : IDisposable
{
    public const string MeterName = "SecondDimensionWatcherReDive.Runtime";
    public const string ActivitySourceName = "SecondDimensionWatcherReDive.Runtime";

    private readonly Meter _meter = new(MeterName);
    private readonly Counter<long> _jobAttempts;
    private readonly Histogram<double> _jobDuration;
    private readonly Counter<long> _scheduledTaskRuns;
    private readonly Histogram<double> _scheduledTaskDuration;
    private int _pendingJobs;
    private int _processingJobs;
    private int _deadLetterJobs;
    private double _oldestPendingAge;

    public RuntimeTelemetry()
    {
        _jobAttempts = _meter.CreateCounter<long>(
            "sdw.durable_job.attempts",
            "{attempt}",
            "Durable job execution attempts.");
        _jobDuration = _meter.CreateHistogram<double>(
            "sdw.durable_job.duration",
            "s",
            "Durable job execution duration.");
        _scheduledTaskRuns = _meter.CreateCounter<long>(
            "sdw.scheduled_task.runs",
            "{run}",
            "Scheduled task run outcomes.");
        _scheduledTaskDuration = _meter.CreateHistogram<double>(
            "sdw.scheduled_task.duration",
            "s",
            "Scheduled task request duration.");
        _meter.CreateObservableGauge(
            "sdw.durable_jobs",
            ObserveJobCounts,
            "{job}",
            "Current durable job counts by status.");
        _meter.CreateObservableGauge(
            "sdw.durable_job.oldest_pending_age",
            () => Volatile.Read(ref _oldestPendingAge),
            "s",
            "Age of the oldest pending durable job.");
    }

    public static Activity? StartDurableJob(DurableJob job)
    {
        var activity = TelemetryActivitySource.Instance.StartActivity(
            "durable_job.process",
            ActivityKind.Consumer);
        activity?.SetTag("job.type", ToTag(job.Type));
        activity?.SetTag("job.stage", ToTag(job.Stage));
        return activity;
    }

    public void RecordJobAttempt(
        DurableJobType type,
        DurableJobStage stage,
        string outcome,
        TimeSpan duration)
    {
        var tags = new TagList
        {
            { "job.type", ToTag(type) },
            { "job.stage", ToTag(stage) },
            { "outcome", outcome }
        };
        _jobAttempts.Add(1, tags);
        _jobDuration.Record(duration.TotalSeconds, tags);
    }

    public void UpdateJobStatistics(DurableJobStatistics statistics)
    {
        Volatile.Write(ref _pendingJobs, statistics.PendingCount);
        Volatile.Write(ref _processingJobs, statistics.ProcessingCount);
        Volatile.Write(ref _deadLetterJobs, statistics.DeadLetterCount);
        Volatile.Write(ref _oldestPendingAge, statistics.OldestPendingAgeSeconds);
    }

    public void RecordScheduledTask(
        string taskId,
        string outcome,
        TimeSpan duration)
    {
        var tags = new TagList
        {
            { "task.id", NormalizeTaskId(taskId) },
            { "outcome", outcome }
        };
        _scheduledTaskRuns.Add(1, tags);
        _scheduledTaskDuration.Record(duration.TotalSeconds, tags);
    }

    public void Dispose() => _meter.Dispose();

    private IEnumerable<Measurement<int>> ObserveJobCounts()
    {
        yield return new Measurement<int>(
            Volatile.Read(ref _pendingJobs),
            new KeyValuePair<string, object?>("status", "pending"));
        yield return new Measurement<int>(
            Volatile.Read(ref _processingJobs),
            new KeyValuePair<string, object?>("status", "processing"));
        yield return new Measurement<int>(
            Volatile.Read(ref _deadLetterJobs),
            new KeyValuePair<string, object?>("status", "dead_letter"));
    }

    private static string ToTag(DurableJobType type) => type switch
    {
        DurableJobType.DownloadCompletion => "download_completion",
        _ => "unknown"
    };

    private static string ToTag(DurableJobStage stage) => stage switch
    {
        DurableJobStage.MapFiles => "map_files",
        DurableJobStage.Notify => "notify",
        DurableJobStage.InvokePlugins => "invoke_plugins",
        DurableJobStage.Done => "done",
        _ => "unknown"
    };

    private static string NormalizeTaskId(string taskId) => taskId switch
    {
        "SyncFeed" => "sync_feed",
        "ScrapeSeasonBangumi" => "scrape_season_bangumi",
        "ScanMediaLibraries" => "scan_media_libraries",
        "InferAnimationMetadata" => "infer_animation_metadata",
        _ => "other"
    };

    private static class TelemetryActivitySource
    {
        internal static readonly ActivitySource Instance = new(ActivitySourceName);
    }
}
