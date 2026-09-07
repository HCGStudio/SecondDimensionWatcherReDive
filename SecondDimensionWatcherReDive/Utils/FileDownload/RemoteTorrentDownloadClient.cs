using System.Threading.Channels;
using System.Text.Json;
using SecondDimensionWatcherReDive.Data;
using SecondDimensionWatcherReDive.Framework.FileDownload;
using SecondDimensionWatcherReDive.Framework.FileStore;

namespace SecondDimensionWatcherReDive.Utils.FileDownload;

/// <summary>
///     Provides an implementation of TorrentDownloadClient that performs remote torrent download operations.
/// </summary>
public class RemoteTorrentDownloadClient(
    IHttpClientFactory httpClientFactory,
    IConfiguration configuration,
    Channel<RemoteTorrentTrackRequest> remoteTorrentTrackRequest,
    DownloadCapacityService? capacity = null)
    : TorrentDownloadClient
{
    public override string Name => FileDownloads.RemoteTorrentDownload;
    public override string SupportedFileStoreType => FileStores.LocalDiskStore;

    public override async Task<bool> SubmitDownloadTaskAsync(
        Guid itemId,
        string downloadUrl,
        byte[] cachedDownloadData,
        string additionalDownloadInfo,
        CancellationToken cancellationToken)
    {
        if (capacity is { Enabled: true })
        {
            await capacity.EnqueueAsync(itemId, cancellationToken);
            return true;
        }
        return await SubmitAdmittedAsync(itemId, cachedDownloadData, additionalDownloadInfo, cancellationToken);
    }

    internal async Task<bool> SubmitAdmittedAsync(
        Guid itemId, byte[] cachedDownloadData, string additionalDownloadInfo, CancellationToken cancellationToken)
    {
        using var client = httpClientFactory.CreateClient(nameof(RemoteTorrentDownloadClient));
        var submission = await SubmitRemoteAsync(
            client,
            itemId,
            cachedDownloadData,
            additionalDownloadInfo,
            cancellationToken);
        if (submission == RemoteSubmissionOutcome.Rejected)
            return false;
        if (submission == RemoteSubmissionOutcome.Unknown)
            throw new HttpRequestException("The qBittorrent submission outcome could not be confirmed.");

        await remoteTorrentTrackRequest.Writer.WriteAsync(
            new(itemId, additionalDownloadInfo),
            cancellationToken);
        return true;
    }

    public override async Task<DownloadTaskReconciliationOutcome> EnsureDownloadTaskAsync(
        Guid itemId,
        string downloadUrl,
        byte[] cachedDownloadData,
        string additionalDownloadInfo,
        CancellationToken cancellationToken)
    {
        if (capacity is { Enabled: true })
        {
            await capacity.EnqueueAsync(itemId, cancellationToken);
            return DownloadTaskReconciliationOutcome.Confirmed;
        }
        try
        {
            using var client = httpClientFactory.CreateClient(nameof(RemoteTorrentDownloadClient));
            if (await IsTorrentPresentAsync(client, additionalDownloadInfo, cancellationToken))
            {
                Track(itemId, additionalDownloadInfo);
                return DownloadTaskReconciliationOutcome.Confirmed;
            }

            var submission = await SubmitRemoteAsync(
                client,
                itemId,
                cachedDownloadData,
                additionalDownloadInfo,
                cancellationToken);
            if (await ConfirmTorrentAsync(client, additionalDownloadInfo, cancellationToken))
            {
                Track(itemId, additionalDownloadInfo);
                return DownloadTaskReconciliationOutcome.Confirmed;
            }

            return submission == RemoteSubmissionOutcome.Rejected
                ? DownloadTaskReconciliationOutcome.Rejected
                : DownloadTaskReconciliationOutcome.Unknown;
        }
        catch (Exception exception) when (exception is HttpRequestException or
                                          OperationCanceledException or
                                          JsonException or
                                          NotSupportedException)
        {
            Track(itemId, additionalDownloadInfo);
            return DownloadTaskReconciliationOutcome.Unknown;
        }
    }

    private static async Task<bool> ConfirmTorrentAsync(
        HttpClient client,
        string infoHash,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            if (await IsTorrentPresentAsync(client, infoHash, cancellationToken))
                return true;
            if (attempt < 2)
                await Task.Delay(TimeSpan.FromMilliseconds(250 * (attempt + 1)), cancellationToken);
        }

        return false;
    }

    private async Task<RemoteSubmissionOutcome> SubmitRemoteAsync(
        HttpClient client,
        Guid itemId,
        byte[] cachedDownloadData,
        string additionalDownloadInfo,
        CancellationToken cancellationToken)
    {
        using var content = new MultipartFormDataContent();
        content.Add(new ByteArrayContent(cachedDownloadData), "torrent", $"{itemId}.torrent");
        var basePath = Path.GetFullPath(configuration["FileStore:Local"] ?? "./download");
        var savePath = Path.Combine(basePath, additionalDownloadInfo);
        content.Add(new StringContent(savePath), "savepath");
        content.Add(new StringContent("false"), "autoTMM");
        using var response = await client.PostAsync(
            "/api/v2/torrents/add",
            content,
            cancellationToken);
        var responseText = (await response.Content.ReadAsStringAsync(cancellationToken)).Trim();
        if (!response.IsSuccessStatusCode)
            return (int)response.StatusCode == StatusCodes.Status415UnsupportedMediaType
                ? RemoteSubmissionOutcome.Rejected
                : RemoteSubmissionOutcome.Unknown;
        if (string.Equals(responseText, "Fails.", StringComparison.OrdinalIgnoreCase))
            return RemoteSubmissionOutcome.Rejected;
        return string.Equals(responseText, "Ok.", StringComparison.OrdinalIgnoreCase)
            ? RemoteSubmissionOutcome.Accepted
            : RemoteSubmissionOutcome.Unknown;
    }

    private static async Task<bool> IsTorrentPresentAsync(
        HttpClient client,
        string infoHash,
        CancellationToken cancellationToken)
    {
        var torrents = await client.GetFromJsonAsync(
            $"/api/v2/torrents/info?hashes={Uri.EscapeDataString(infoHash)}",
            QBittorrentJsonSerializerContext.Default.RemoteTorrentInfoArray,
            cancellationToken);
        return torrents?.Any(torrent => string.Equals(
            torrent.Hash,
            infoHash,
            StringComparison.OrdinalIgnoreCase)) == true;
    }

    private void Track(Guid itemId, string infoHash) =>
        remoteTorrentTrackRequest.Writer.TryWrite(new(itemId, infoHash));

    private enum RemoteSubmissionOutcome
    {
        Accepted,
        Rejected,
        Unknown
    }

    public override async Task SubmitQueryDownloadProgressAsync(
        Guid itemId,
        string downloadUrl,
        byte[] cachedDownloadData,
        string additionalDownloadInfo,
        CancellationToken cancellationToken)
    {
        await remoteTorrentTrackRequest.Writer.WriteAsync(
            new(itemId, additionalDownloadInfo),
            cancellationToken);
    }

    public override async Task<bool> PauseDownloadTaskAsync(
        Guid itemId,
        string downloadUrl,
        byte[] cachedDownloadData,
        string additionalDownloadInfo,
        CancellationToken cancellationToken)
    {
        if (capacity is { Enabled: true })
            return await capacity.ControlAsync(itemId, "pause",
                token => SetRemotePausedAsync(additionalDownloadInfo, true, token), cancellationToken);
        return await SetRemotePausedAsync(additionalDownloadInfo, true, cancellationToken);
    }

    private async Task<bool> SetRemotePausedAsync(string hash, bool paused, CancellationToken cancellationToken)
    {
        using var content =
            new FormUrlEncodedContent([new("hashes", hash)]);
        using var client = httpClientFactory.CreateClient(nameof(RemoteTorrentDownloadClient));
        using var response = await client.PostAsync(
            paused ? "/api/v2/torrents/stop" : "/api/v2/torrents/start",
            content,
            cancellationToken);
        return response.IsSuccessStatusCode;
    }

    public override async Task<bool> ResumeDownloadTaskAsync(
        Guid itemId,
        string downloadUrl,
        byte[] cachedDownloadData,
        string additionalDownloadInfo,
        CancellationToken cancellationToken)
    {
        var result = capacity is { Enabled: true }
            ? await capacity.ControlAsync(itemId, "resume",
                token => SetRemotePausedAsync(additionalDownloadInfo, false, token), cancellationToken)
            : await SetRemotePausedAsync(additionalDownloadInfo, false, cancellationToken);
        if (result) Track(itemId, additionalDownloadInfo);
        return result;
    }

    public override async Task<CancelDownloadResult> CancelDownloadTaskAsync(
        Guid itemId,
        string downloadUrl,
        byte[] cachedDownloadData,
        string additionalDownloadInfo,
        bool removeFile,
        CancellationToken cancellationToken)
    {
        var result = capacity is { Enabled: true }
            ? await capacity.ControlAsync(itemId, "cancel",
                token => DeleteRemoteAsync(additionalDownloadInfo, removeFile, token), cancellationToken)
            : await DeleteRemoteAsync(additionalDownloadInfo, removeFile, cancellationToken);
        if (result)
            await remoteTorrentTrackRequest.Writer.WriteAsync(
                new RemoteTorrentTrackRequest(itemId, additionalDownloadInfo, Remove: true), cancellationToken);
        return new(result, false);
    }

    private async Task<bool> DeleteRemoteAsync(string hash, bool removeFile, CancellationToken cancellationToken)
    {
        var deleteFiles = removeFile ? "true" : "false";
        using var content = new FormUrlEncodedContent([
            new("hashes", hash),
            new("deleteFiles", deleteFiles)
        ]);
        using var client = httpClientFactory.CreateClient(nameof(RemoteTorrentDownloadClient));
        using var response = await client.PostAsync(
            $"/api/v2/torrents/delete",
            content,
            cancellationToken);

        return response.IsSuccessStatusCode;
    }
}
