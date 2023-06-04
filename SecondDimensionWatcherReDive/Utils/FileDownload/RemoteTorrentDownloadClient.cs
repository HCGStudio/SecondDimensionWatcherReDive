using System.Threading.Channels;
using SecondDimensionWatcherReDive.Data;
using SecondDimensionWatcherReDive.Utils.FileStore;

namespace SecondDimensionWatcherReDive.Utils.FileDownload;

/// <summary>
///     Provides an implementation of TorrentDownloadClient that performs remote torrent download operations.
/// </summary>
public class RemoteTorrentDownloadClient(
    IHttpClientFactory httpClientFactory,
    IConfiguration configuration,
    Channel<RemoteTorrentTrackRequest> remoteTorrentTrackRequest)
    : TorrentDownloadClient
{
    private readonly HttpClient _httpClient = httpClientFactory.CreateClient(nameof(RemoteTorrentDownloadClient));

    public override string Name => FileDownloads.RemoteTorrentDownload;
    public override string SupportedFileStoreType => FileStores.LocalDiskStore;

    public override async Task<bool> SubmitDownloadTask(
        Guid itemId,
        string downloadUrl,
        byte[] cachedDownloadData,
        string additionalDownloadInfo)
    {
        using var content = new MultipartFormDataContent();
        content.Add(new ByteArrayContent(cachedDownloadData), "torrent", $"{itemId}.torrent");
        content.Add(new StringContent(Path.GetFullPath(configuration["FileStore:Local"] ?? "./download")), "savepath");

        using var response = await _httpClient.PostAsync("/api/v2/torrents/add", content);

        if (response.IsSuccessStatusCode)
            await remoteTorrentTrackRequest.Writer.WriteAsync(
                new RemoteTorrentTrackRequest(itemId, additionalDownloadInfo));

        return response.IsSuccessStatusCode;
    }

    public override async Task SubmitQueryDownloadProgress(
        Guid itemId,
        string downloadUrl,
        byte[] cachedDownloadData,
        string additionalDownloadInfo)
    {
        await remoteTorrentTrackRequest.Writer.WriteAsync(
            new RemoteTorrentTrackRequest(itemId, additionalDownloadInfo));
    }

    public override async Task<bool> PauseDownloadTask(
        Guid itemId,
        string downloadUrl,
        byte[] cachedDownloadData,
        string additionalDownloadInfo)
    {
        using var content =
            new FormUrlEncodedContent([new KeyValuePair<string, string>("hashes", additionalDownloadInfo)]);
        using var response = await _httpClient.PostAsync("/api/v2/torrents/pause?", content);
        return response.IsSuccessStatusCode;
    }

    public override async Task<bool> ResumeDownloadTask(
        Guid itemId,
        string downloadUrl,
        byte[] cachedDownloadData,
        string additionalDownloadInfo)
    {
        using var content =
            new FormUrlEncodedContent([new KeyValuePair<string, string>("hashes", additionalDownloadInfo)]);
        using var response = await _httpClient.PostAsync("/api/v2/torrents/resume?", content);
        return response.IsSuccessStatusCode;
    }
}