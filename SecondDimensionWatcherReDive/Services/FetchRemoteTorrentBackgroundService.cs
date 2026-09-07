using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using SecondDimensionWatcherReDive.Data;
using SecondDimensionWatcherReDive.Framework.FileStore;
using SecondDimensionWatcherReDive.Framework.DataRepository;
using SecondDimensionWatcherReDive.Framework.Notifications;
using SecondDimensionWatcherReDive.Utils.FileDownload;
using SecondDimensionWatcherReDive.Utils.Incidents;

namespace SecondDimensionWatcherReDive.Services;

public partial class FetchRemoteTorrentBackgroundService(
    Channel<RemoteTorrentTrackRequest> remoteTorrentTrackRequest,
    IHttpClientFactory httpClientFactory,
    Channel<DownloadCompleteRequest> downloadCompleteRequest,
    Channel<FileDownloadStatus> fileDownloadStatus,
    IServiceScopeFactory scopeFactory,
    ILogger<FetchRemoteTorrentBackgroundService> logger,
    IConfiguration configuration,
    IIncidentReporter? incidentReporter = null,
    INotificationPublisher? notificationPublisher = null)
    : BackgroundService
{
    private sealed record DownloadObservation(
        Guid ItemId,
        double Progress,
        DateTimeOffset LastProgressAt,
        DateTimeOffset? LastReportedAt,
        DateTimeOffset? LastResolvedAt);

    private sealed class PollSchedule
    {
        public DateTimeOffset NextDue { get; set; }
        public DateTimeOffset LastChange { get; set; } = DateTimeOffset.UtcNow;
        public DateTimeOffset LastEmittedAt { get; set; }
        public FileDownloadStatus? LastEmitted { get; set; }
        public double? LastProgress { get; set; }
        public int Failures { get; set; }
    }

    private async IAsyncEnumerable<RemoteTorrentTrackRequest> FetchUnfinishedTaskFromDb(
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var animationInfoRepository = scope.ServiceProvider.GetRequiredService<IAnimationInfoRepository>();

        await foreach (var info in animationInfoRepository.GetUnfinishedTorrentDownloadsAsync(cancellationToken))
            yield return new RemoteTorrentTrackRequest(
                info.Id,
                info.AdditionalDownloadInfo,
                DownloadAttemptId: info.DownloadAttemptId);
    }

    private async Task<RemoteTorrentTrackRequest?> BindCurrentAttemptAsync(
        RemoteTorrentTrackRequest request,
        CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var repository = scope.ServiceProvider.GetRequiredService<IAnimationInfoRepository>();
        var info = await repository.FindByIdAsync(request.ItemId, cancellationToken);
        if (info is null
            || !info.IsDownloadTracked
            || !string.Equals(
                info.AdditionalDownloadInfo,
                request.Hash,
                StringComparison.OrdinalIgnoreCase))
            return null;

        if (scope.ServiceProvider.GetService<IDownloadCapacityRepository>() is { } capacity)
        {
            var queued = (await capacity.ListAsync(cancellationToken)).FirstOrDefault(entry => entry.ItemId == info.Id);
            if (queued is not null && queued.State != "Submitted") return null;
        }

        return request with { DownloadAttemptId = info.DownloadAttemptId };
    }

    protected override async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        var reader = remoteTorrentTrackRequest.Reader;
        var tracked = new ConcurrentDictionary<string, RemoteTorrentTrackRequest>(StringComparer.OrdinalIgnoreCase);
        var observations = new ConcurrentDictionary<string, DownloadObservation>(StringComparer.OrdinalIgnoreCase);
        var nextDatabaseRefreshAt = DateTimeOffset.MinValue;
        var schedules = new Dictionary<string, PollSchedule>(StringComparer.OrdinalIgnoreCase);
        var capacityWaiting = new HashSet<Guid>();
        var activeInterval = TimeSpan.FromMilliseconds(Math.Clamp(configuration.GetValue("Torrent:Polling:ActiveMilliseconds", 1000), 500, 5000));
        var pausedInterval = TimeSpan.FromSeconds(Math.Clamp(configuration.GetValue("Torrent:Polling:PausedSeconds", 30), 5, 120));
        var idleInterval = TimeSpan.FromSeconds(Math.Clamp(configuration.GetValue("Torrent:Polling:IdleSeconds", 10), 2, 60));
        var batchSize = Math.Clamp(configuration.GetValue("Torrent:Polling:BatchSize", 50), 1, 100);
        var maxBackoff = Math.Clamp(configuration.GetValue("Torrent:Polling:MaxBackoffSeconds", 120), 5, 600);
        var statusTimeout = TimeSpan.FromSeconds(Math.Clamp(configuration.GetValue("Torrent:Polling:StatusTimeoutSeconds", 30), 1, 600));

        while (!cancellationToken.IsCancellationRequested)
        {
            if (DateTimeOffset.UtcNow >= nextDatabaseRefreshAt)
            {
                try
                {
                    // Periodic refresh recovers requests whose initial channel
                    // binding happened during a temporary database outage.
                    await using var capacityScope = scopeFactory.CreateAsyncScope();
                    var capacityRepository = capacityScope.ServiceProvider.GetService<IDownloadCapacityRepository>();
                    capacityWaiting = capacityRepository is null ? [] :
                        (await capacityRepository.ListAsync(cancellationToken))
                        .Where(entry => entry.State != "Submitted").Select(entry => entry.ItemId).ToHashSet();
                    var recovered = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    await foreach (var request in FetchUnfinishedTaskFromDb(cancellationToken))
                    {
                        if (capacityWaiting.Contains(request.ItemId)) continue;
                        recovered.Add(request.Hash);
                        if (tracked.TryGetValue(request.Hash, out var previous) && previous.DownloadAttemptId != request.DownloadAttemptId)
                        {
                            observations.TryRemove(request.Hash, out _);
                            schedules.Remove(request.Hash);
                        }
                        tracked[request.Hash] = request;
                    }
                    // A tracked attempt can return to the capacity queue. Only
                    // reconcile removals after a complete successful DB refresh.
                    foreach (var hash in tracked.Keys.Where(hash => !recovered.Contains(hash)))
                    {
                        tracked.TryRemove(hash, out _);
                        observations.TryRemove(hash, out _);
                        schedules.Remove(hash);
                    }
                    nextDatabaseRefreshAt = DateTimeOffset.UtcNow.AddSeconds(30);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    nextDatabaseRefreshAt = DateTimeOffset.UtcNow.AddSeconds(5);
                    LogRefreshTrackedDownloadsFailed(logger, exception);
                }
            }

            // Drain channel messages in the supervised service loop so channel
            // failures/cancellation cannot disappear in an unobserved Task.
            while (reader.TryRead(out var request))
            {
                if (request.Remove)
                {
                    tracked.TryRemove(request.Hash, out _);
                    observations.TryRemove(request.Hash, out _);
                    schedules.Remove(request.Hash);
                    await ResolveDownloadIncidentAsync(request.ItemId, cancellationToken);
                }
                else
                {
                    try
                    {
                        var boundRequest = await BindCurrentAttemptAsync(request, cancellationToken);
                        if (boundRequest is { } currentRequest)
                        {
                            tracked[request.Hash] = currentRequest;
                            // Submission and user resume wake a slow/paused schedule.
                            schedules[request.Hash] = new PollSchedule();
                            capacityWaiting.Remove(request.ItemId);
                        }
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception exception)
                    {
                        // The periodic database refresh above will recover this
                        // unfinished attempt without relying on an unbounded queue.
                        nextDatabaseRefreshAt = DateTimeOffset.MinValue;
                        LogBindTrackedDownloadFailed(logger, exception, request.ItemId);
                    }
                }
            }
            // Process one oldest-due batch per turn so submission, resume and
            // cancellation messages are drained between slow status requests.
            var batch = tracked.Keys.Where(hash => !schedules.TryGetValue(hash, out var schedule)
                || schedule.NextDue <= DateTimeOffset.UtcNow)
                .OrderBy(hash => schedules.TryGetValue(hash, out var schedule) ? schedule.NextDue : DateTimeOffset.MinValue)
                .Take(batchSize).ToArray();
            if (batch.Length == 0)
            {
                await Task.Delay(500, cancellationToken);
                continue;
            }

            RemoteTorrentInfo[] info;
            var queried = new ConcurrentDictionary<string, RemoteTorrentTrackRequest>(StringComparer.OrdinalIgnoreCase);
            foreach (var hash in batch)
            {
                if (tracked.TryGetValue(hash, out var request)) queried[hash] = request;
                schedules.TryAdd(hash, new PollSchedule());
            }
            try
            {
                using var httpClient = httpClientFactory.CreateClient(nameof(RemoteTorrentDownloadClient));
                using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                // Include authentication and the shared request lock in the
                // configurable deadline without HttpClient imposing a shorter limit.
                httpClient.Timeout = Timeout.InfiniteTimeSpan;
                deadline.CancelAfter(statusTimeout);
                info = await httpClient.GetFromJsonAsync(
                    $"/api/v2/torrents/info?hashes={Uri.EscapeDataString(string.Join('|', batch))}",
                    QBittorrentJsonSerializerContext.Default.RemoteTorrentInfoArray,
                    deadline.Token) ?? throw new IOException("The downloader returned an empty status response.");
                foreach (var hash in batch)
                {
                    schedules[hash].Failures = 0;
                    schedules[hash].NextDue = DateTimeOffset.UtcNow + activeInterval;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                LogFetchTorrentStatusFailed(logger, ex);
                foreach (var hash in batch)
                {
                    var schedule = schedules[hash];
                    schedule.Failures = Math.Min(20, schedule.Failures + 1);
                    var seconds = Math.Min(maxBackoff, Math.Pow(2, schedule.Failures));
                    schedule.NextDue = DateTimeOffset.UtcNow.AddSeconds(seconds * (0.8 + Random.Shared.NextDouble() * 0.2));
                }
                // Transport failure says nothing about whether any hash exists.
                // Other batches still run and retain their own failure history.
                continue;
            }

            var returnedHashes = new HashSet<string>(
                info.Select(torrent => torrent.Hash),
                StringComparer.OrdinalIgnoreCase);

            foreach (var torrentInfo in info)
            {
                if (!queried.TryGetValue(torrentInfo.Hash, out var request)) continue;
                var state = torrentInfo.State.ToDownloadState();

                var schedule = schedules[torrentInfo.Hash];
                var now = DateTimeOffset.UtcNow;
                if (schedule.LastProgress is null || Math.Abs(torrentInfo.Progress - schedule.LastProgress.Value) > 0.000001
                    || torrentInfo.Speed > 0)
                    schedule.LastChange = now;
                schedule.LastProgress = torrentInfo.Progress;
                schedule.NextDue = now + (state == FileDownloadState.Paused ? pausedInterval :
                    now - schedule.LastChange >= TimeSpan.FromMinutes(1) ? idleInterval : activeInterval);
                var status = new FileDownloadStatus(request.ItemId, torrentInfo.Progress, torrentInfo.Eta, torrentInfo.Speed, state);
                var previous = schedule.LastEmitted;
                // State and meaningful progress are immediate. Instantaneous speed
                // changes coalesce at 64 KiB/s or 10%; at least one update per 10 s.
                if (previous is null || previous.State != state || state == FileDownloadState.Finished
                    || Math.Abs(previous.Progress - status.Progress) >= 0.001
                    || now - schedule.LastEmittedAt >= TimeSpan.FromSeconds(10)
                    || Math.Abs((long)previous.Speed - status.Speed) >= Math.Max(65536, previous.Speed * 0.1))
                {
                    await fileDownloadStatus.Writer.WriteAsync(status, cancellationToken);
                    schedule.LastEmitted = status;
                    schedule.LastEmittedAt = now;
                }

                await ObserveHealthAsync(
                    request,
                    torrentInfo,
                    state,
                    observations,
                    cancellationToken);

                if (state != FileDownloadState.Finished) continue;

                var completion = new DownloadCompleteRequest(
                    request.ItemId,
                    torrentInfo.SavePath,
                    FileStores.LocalDiskStore,
                    request.DownloadAttemptId);
                try
                {
                    // The completion transition and its durable workflow are committed
                    // together. The channel is only a best-effort wake-up signal; the
                    // durable worker also polls after restart.
                    await PersistCompletionAsync(completion, cancellationToken);
                    downloadCompleteRequest.Writer.TryWrite(completion);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    LogPersistCompletionFailed(logger, exception, request.ItemId);
                    continue;
                }
                tracked.TryRemove(torrentInfo.Hash, out _);
                observations.TryRemove(torrentInfo.Hash, out _);
                schedules.Remove(torrentInfo.Hash);
                queried.TryRemove(torrentInfo.Hash, out _);
            }

            await ReportMissingAfterThresholdAsync(
                queried,
                observations,
                returnedHashes,
                cancellationToken);
        }
    }

    private async Task PersistCompletionAsync(
        DownloadCompleteRequest request,
        CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var repository = scope.ServiceProvider.GetRequiredService<IAnimationInfoRepository>();
        await repository.TryCompleteDownloadAsync(
            request.ItemId,
            request.DownloadAttemptId,
            request.FileStore,
            request.StorePath,
            DateTimeOffset.UtcNow,
            cancellationToken);
    }

    private async Task ObserveHealthAsync(
        RemoteTorrentTrackRequest request,
        RemoteTorrentInfo torrentInfo,
        FileDownloadState state,
        ConcurrentDictionary<string, DownloadObservation> observations,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var observation = observations.GetOrAdd(
            torrentInfo.Hash,
            _ => new DownloadObservation(request.ItemId, torrentInfo.Progress, now, null, null));

        if (state is FileDownloadState.Finished or FileDownloadState.Paused)
        {
            if (ShouldResolve(observation, now))
            {
                await ResolveDownloadIncidentAsync(request.ItemId, cancellationToken);
                observation = observation with { LastResolvedAt = now };
            }
            observations[torrentInfo.Hash] = observation with
            {
                Progress = torrentInfo.Progress,
                LastProgressAt = now,
                LastReportedAt = null
            };
            return;
        }

        if (state == FileDownloadState.Error)
        {
            if (!ShouldReport(observation, now)) return;
            await ReportDownloadIncidentAsync(
                request.ItemId,
                $"The remote download client reports state '{torrentInfo.State}'.",
                cancellationToken);
            if (notificationPublisher is not null)
            {
                await notificationPublisher.PublishAsync(new NotificationEvent(
                    NotificationEventType.DownloadFailed,
                    $"download-failed:{request.ItemId}:{request.DownloadAttemptId?.ToString() ?? "legacy"}",
                    "Download failed",
                    "The remote download client reported an error.",
                    "/downloading"), cancellationToken);
            }
            observations[torrentInfo.Hash] = observation with
            {
                LastReportedAt = now,
                LastResolvedAt = null
            };
            return;
        }

        if (torrentInfo.Progress > observation.Progress + 0.000001 || torrentInfo.Speed > 0)
        {
            if (ShouldResolve(observation, now))
            {
                await ResolveDownloadIncidentAsync(request.ItemId, cancellationToken);
                observation = observation with { LastResolvedAt = now };
            }
            observations[torrentInfo.Hash] = observation with
            {
                Progress = torrentInfo.Progress,
                LastProgressAt = now,
                LastReportedAt = null
            };
            return;
        }

        var stalledAfter = GetStalledAfter(configuration);
        if (now - observation.LastProgressAt >= stalledAfter && ShouldReport(observation, now))
        {
            await ReportDownloadIncidentAsync(
                request.ItemId,
                $"No download progress for {stalledAfter.TotalMinutes:F0} minutes " +
                $"(state: {torrentInfo.State}, progress: {torrentInfo.Progress:P1}).",
                cancellationToken);
            observations[torrentInfo.Hash] = observation with
            {
                LastReportedAt = now,
                LastResolvedAt = null
            };
        }
    }

    private async Task ReportMissingAfterThresholdAsync(
        ConcurrentDictionary<string, RemoteTorrentTrackRequest> tracked,
        ConcurrentDictionary<string, DownloadObservation> observations,
        IReadOnlySet<string> returnedHashes,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var stalledAfter = GetStalledAfter(configuration);
        foreach (var pair in tracked)
        {
            if (returnedHashes.Contains(pair.Key)) continue;
            var observation = observations.GetOrAdd(
                pair.Key,
                _ => new DownloadObservation(pair.Value.ItemId, 0, now, null, null));
            if (now - observation.LastProgressAt < stalledAfter || !ShouldReport(observation, now)) continue;

            await ReportDownloadIncidentAsync(
                pair.Value.ItemId,
                $"The remote download client has not reported this torrent for " +
                $"{stalledAfter.TotalMinutes:F0} minutes.",
                cancellationToken);
            observations[pair.Key] = observation with
            {
                LastReportedAt = now,
                LastResolvedAt = null
            };
        }
    }

    private async Task ReportDownloadIncidentAsync(
        Guid itemId,
        string detail,
        CancellationToken cancellationToken)
    {
        if (incidentReporter is null) return;
        await incidentReporter.ReportAsync(new IncidentReport(
                IncidentType.DownloadStalled,
                IncidentSeverity.Error,
                "Download is stalled",
                detail,
                itemId.ToString()),
            cancellationToken);
    }

    private Task ResolveDownloadIncidentAsync(Guid itemId, CancellationToken cancellationToken)
    {
        return incidentReporter?.ResolveAsync(
                   IncidentType.DownloadStalled,
                   itemId.ToString(),
                   cancellationToken)
               ?? Task.CompletedTask;
    }

    private static TimeSpan GetStalledAfter(IConfiguration configuration)
    {
        var configured = configuration.GetValue<TimeSpan?>("Incidents:DownloadStalledAfter")
                         ?? TimeSpan.FromMinutes(15);
        return configured > TimeSpan.Zero ? configured : TimeSpan.FromMinutes(15);
    }

    private static TimeSpan GetReportThrottle(IConfiguration configuration)
    {
        var configured = configuration.GetValue<TimeSpan?>("Incidents:ReportThrottle")
                         ?? TimeSpan.FromMinutes(5);
        return configured > TimeSpan.Zero ? configured : TimeSpan.FromMinutes(5);
    }

    private bool ShouldReport(DownloadObservation observation, DateTimeOffset now)
    {
        var reportThrottle = GetReportThrottle(configuration);
        return observation.LastReportedAt is null
               || now - observation.LastReportedAt.Value >= reportThrottle;
    }

    private bool ShouldResolve(DownloadObservation observation, DateTimeOffset now)
    {
        var reportThrottle = GetReportThrottle(configuration);
        return observation.LastResolvedAt is null
               || now - observation.LastResolvedAt.Value >= reportThrottle;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to fetch torrent status from remote client")]
    private static partial void LogFetchTorrentStatusFailed(ILogger logger, Exception ex);

    [LoggerMessage(Level = LogLevel.Error,
        Message = "Could not persist durable download completion for {ItemId}; tracking will retry")]
    private static partial void LogPersistCompletionFailed(
        ILogger logger,
        Exception exception,
        Guid itemId);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Could not refresh unfinished downloads; retrying")]
    private static partial void LogRefreshTrackedDownloadsFailed(
        ILogger logger,
        Exception exception);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Could not bind download attempt {ItemId}; the database refresh will retry")]
    private static partial void LogBindTrackedDownloadFailed(
        ILogger logger,
        Exception exception,
        Guid itemId);
}
