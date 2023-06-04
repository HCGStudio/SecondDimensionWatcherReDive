using System.Collections.Concurrent;
using System.Threading.Channels;
using Microsoft.EntityFrameworkCore;
using SecondDimensionWatcherReDive.Data;
using SecondDimensionWatcherReDive.Models;
using SecondDimensionWatcherReDive.Utils.FileDownload;
using SecondDimensionWatcherReDive.Utils.FileStore;

namespace SecondDimensionWatcherReDive.Services;

public class FetchRemoteTorrent : BackgroundService
{
    private readonly Channel<DownloadCompleteRequest> _downloadCompleteRequest;
    private readonly Channel<FileDownloadStatus> _fileDownloadStatus;
    private readonly HttpClient _httpClient;
    private readonly Channel<RemoteTorrentTrackRequest> _remoteTorrentTrackRequest;
    private readonly IServiceScopeFactory _scopeFactory;

    public FetchRemoteTorrent(Channel<RemoteTorrentTrackRequest> remoteTorrentTrackRequest,
        IHttpClientFactory httpClientFactory, Channel<DownloadCompleteRequest> downloadCompleteRequest,
        Channel<FileDownloadStatus> fileDownloadStatus, IServiceScopeFactory scopeFactory)
    {
        _remoteTorrentTrackRequest = remoteTorrentTrackRequest;
        _downloadCompleteRequest = downloadCompleteRequest;
        _fileDownloadStatus = fileDownloadStatus;
        _scopeFactory = scopeFactory;
        _httpClient = httpClientFactory.CreateClient(nameof(RemoteTorrentDownloadClient));
    }

    private async IAsyncEnumerable<RemoteTorrentTrackRequest> FetchUnfinishedTaskFromDb()
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        await using var applicationContext = scope.ServiceProvider.GetRequiredService<ApplicationContext>();
        var unfinished =
            applicationContext.AnimationInfo
                .Where(i => i.IsDownloadTracked
                            && !i.IsDownloadFinished
                            && i.DownloadType == FileDownloadTypes.TorrentDownload)
                .AsAsyncEnumerable();

        await foreach (var info in unfinished)
            yield return new RemoteTorrentTrackRequest(info.Id, info.AdditionalDownloadInfo);
    }

    protected override async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        var reader = _remoteTorrentTrackRequest.Reader;
        var tracked = new ConcurrentDictionary<string, RemoteTorrentTrackRequest>();

        // Add unfinished to track
        await foreach (var request in FetchUnfinishedTaskFromDb().WithCancellation(cancellationToken))
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

            var info = await _httpClient.GetFromJsonAsync<RemoteTorrentInfo[]>(
                $"/api/v2/torrents/info?hashes={string.Join('|', tracked.Keys)}", cancellationToken);
            if (info is null) continue;

            foreach (var torrentInfo in info)
            {
                var request = tracked[torrentInfo.Hash];
                var state = torrentInfo.State.ToDownloadState();

                await _fileDownloadStatus.Writer.WriteAsync(new FileDownloadStatus(request.ItemId, torrentInfo.Progress,
                    torrentInfo.Eta, torrentInfo.Speed,
                    state), cancellationToken);

                if (state != FileDownloadState.Finished) continue;

                //Write complete request and stop tracking.
                await _downloadCompleteRequest.Writer.WriteAsync(
                    new DownloadCompleteRequest(request.ItemId, torrentInfo.SavePath, FileStores.LocalDiskStore),
                    cancellationToken);
                tracked.TryRemove(torrentInfo.Hash, out _);
            }
        }
    }
}