using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using SecondDimensionWatcherReDive.Data;
using SecondDimensionWatcherReDive.Framework.FileStore;
using SecondDimensionWatcherReDive.Framework.DataRepository;
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
    IIncidentReporter? incidentReporter = null)
    : BackgroundService
{
    private sealed record DownloadObservation(
        Guid ItemId,
        double Progress,
        DateTimeOffset LastProgressAt,
        DateTimeOffset? LastReportedAt,
        DateTimeOffset? LastResolvedAt);

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

        return request with { DownloadAttemptId = info.DownloadAttemptId };
    }

    protected override async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        var reader = remoteTorrentTrackRequest.Reader;
        var tracked = new ConcurrentDictionary<string, RemoteTorrentTrackRequest>();
        var observations = new ConcurrentDictionary<string, DownloadObservation>();

        // Add unfinished to track
        await foreach (var request in FetchUnfinishedTaskFromDb(cancellationToken))
            tracked[request.Hash] = request;

        while (!cancellationToken.IsCancellationRequested)
        {
            // Drain channel messages in the supervised service loop so channel
            // failures/cancellation cannot disappear in an unobserved Task.
            while (reader.TryRead(out var request))
            {
                if (request.Remove)
                {
                    tracked.TryRemove(request.Hash, out _);
                    observations.TryRemove(request.Hash, out _);
                    await ResolveDownloadIncidentAsync(request.ItemId, cancellationToken);
                }
                else
                {
                    var boundRequest = await BindCurrentAttemptAsync(request, cancellationToken);
                    if (boundRequest is { } currentRequest)
                        tracked[request.Hash] = currentRequest;
                }
            }
            await Task.Delay(500, cancellationToken);

            //Check if there is no need to update
            if (tracked.Count == 0)
                continue;

            var info = default(RemoteTorrentInfo[]);
            try
            {
                using var httpClient = httpClientFactory.CreateClient(nameof(RemoteTorrentDownloadClient));
                info = await httpClient.GetFromJsonAsync(
                    $"/api/v2/torrents/info?hashes={string.Join('|', tracked.Keys)}",
                    QBittorrentJsonSerializerContext.Default.RemoteTorrentInfoArray,
                    cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogFetchTorrentStatusFailed(logger, ex);
                await ReportMissingAfterThresholdAsync(
                    tracked,
                    observations,
                    new HashSet<string>(StringComparer.OrdinalIgnoreCase),
                    cancellationToken);
                continue;
            }

            if (info is null)
            {
                await ReportMissingAfterThresholdAsync(
                    tracked,
                    observations,
                    new HashSet<string>(StringComparer.OrdinalIgnoreCase),
                    cancellationToken);
                continue;
            }

            var returnedHashes = new HashSet<string>(
                info.Select(torrent => torrent.Hash),
                StringComparer.OrdinalIgnoreCase);

            foreach (var torrentInfo in info)
            {
                if (!tracked.TryGetValue(torrentInfo.Hash, out var request)) continue;
                var state = torrentInfo.State.ToDownloadState();

                await fileDownloadStatus.Writer.WriteAsync(new FileDownloadStatus(request.ItemId, torrentInfo.Progress,
                    torrentInfo.Eta, torrentInfo.Speed,
                    state), cancellationToken);

                await ObserveHealthAsync(
                    request,
                    torrentInfo,
                    state,
                    observations,
                    cancellationToken);

                if (state != FileDownloadState.Finished) continue;

                //Write complete request and stop tracking.
                await downloadCompleteRequest.Writer.WriteAsync(
                    new DownloadCompleteRequest(
                        request.ItemId,
                        torrentInfo.SavePath,
                        FileStores.LocalDiskStore,
                        request.DownloadAttemptId),
                    cancellationToken);
                tracked.TryRemove(torrentInfo.Hash, out _);
                observations.TryRemove(torrentInfo.Hash, out _);
            }

            await ReportMissingAfterThresholdAsync(
                tracked,
                observations,
                returnedHashes,
                cancellationToken);
        }
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
}
