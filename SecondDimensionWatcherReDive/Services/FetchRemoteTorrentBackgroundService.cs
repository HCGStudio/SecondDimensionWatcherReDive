using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using SecondDimensionWatcherReDive.Data;
using SecondDimensionWatcherReDive.Framework.FileStore;
using SecondDimensionWatcherReDive.Framework.DataRepository;
using SecondDimensionWatcherReDive.Utils.FileDownload;

namespace SecondDimensionWatcherReDive.Services;

public partial class FetchRemoteTorrentBackgroundService(
    Channel<RemoteTorrentTrackRequest> remoteTorrentTrackRequest,
    IHttpClientFactory httpClientFactory,
    Channel<DownloadCompleteRequest> downloadCompleteRequest,
    Channel<FileDownloadStatus> fileDownloadStatus,
    IServiceScopeFactory scopeFactory,
    ILogger<FetchRemoteTorrentBackgroundService> logger)
    : BackgroundService
{
    private readonly HttpClient _httpClient = httpClientFactory.CreateClient(nameof(RemoteTorrentDownloadClient));

    private async IAsyncEnumerable<RemoteTorrentTrackRequest> FetchUnfinishedTaskFromDb(
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var animationInfoRepository = scope.ServiceProvider.GetRequiredService<IAnimationInfoRepository>();

        await foreach (var info in animationInfoRepository.GetUnfinishedTorrentDownloadsAsync(cancellationToken))
            yield return new RemoteTorrentTrackRequest(info.Id, info.AdditionalDownloadInfo);
    }

    protected override async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        var reader = remoteTorrentTrackRequest.Reader;
        var tracked = new ConcurrentDictionary<string, RemoteTorrentTrackRequest>();

        // Add unfinished to track
        await foreach (var request in FetchUnfinishedTaskFromDb(cancellationToken))
            tracked[request.Hash] = request;

        _ = Task.Run(async () =>
        {
            //Add to track list
            await foreach (var request in reader.ReadAllAsync(cancellationToken))
                tracked[request.Hash] = request;
        }, cancellationToken);

        while (!cancellationToken.IsCancellationRequested)
        {
            await Task.Delay(500, cancellationToken);

            //Check if there is no need to update
            if (tracked.Count == 0)
                continue;

            var info = default(RemoteTorrentInfo[]);
            try
            {
                info = await _httpClient.GetFromJsonAsync(
                    $"/api/v2/torrents/info?hashes={string.Join('|', tracked.Keys)}",
                    QBittorrentJsonSerializerContext.Default.RemoteTorrentInfoArray,
                    cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogFetchTorrentStatusFailed(logger, ex);
                continue;
            }

            if (info is null) continue;

            foreach (var torrentInfo in info)
            {
                var request = tracked[torrentInfo.Hash];
                var state = torrentInfo.State.ToDownloadState();

                await fileDownloadStatus.Writer.WriteAsync(new FileDownloadStatus(request.ItemId, torrentInfo.Progress,
                    torrentInfo.Eta, torrentInfo.Speed,
                    state), cancellationToken);

                if (state != FileDownloadState.Finished) continue;

                //Write complete request and stop tracking.
                await downloadCompleteRequest.Writer.WriteAsync(
                    new DownloadCompleteRequest(request.ItemId, torrentInfo.SavePath, FileStores.LocalDiskStore),
                    cancellationToken);
                tracked.TryRemove(torrentInfo.Hash, out _);
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to fetch torrent status from remote client")]
    private static partial void LogFetchTorrentStatusFailed(ILogger logger, Exception ex);
}
