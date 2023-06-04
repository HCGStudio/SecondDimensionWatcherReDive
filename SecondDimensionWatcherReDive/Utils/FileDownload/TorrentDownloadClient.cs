namespace SecondDimensionWatcherReDive.Utils.FileDownload;

/// <summary>
///     Provides an abstract base for clients that perform torrent file download operations.
/// </summary>
public abstract class TorrentDownloadClient : IFileDownloadClient
{
    public abstract string Name { get; }
    public abstract string SupportedFileStoreType { get; }
    public string FileDownloadType => FileDownloadTypes.TorrentDownload;

    public abstract Task<bool> SubmitDownloadTask(Guid itemId, string downloadUrl, byte[] cachedDownloadData,
        string additionalDownloadInfo);

    public abstract Task SubmitQueryDownloadProgress(Guid itemId, string downloadUrl, byte[] cachedDownloadData,
        string additionalDownloadInfo);

    public abstract Task<bool> PauseDownloadTask(Guid itemId, string downloadUrl, byte[] cachedDownloadData,
        string additionalDownloadInfo);

    public abstract Task<bool> ResumeDownloadTask(Guid itemId, string downloadUrl, byte[] cachedDownloadData,
        string additionalDownloadInfo);
}