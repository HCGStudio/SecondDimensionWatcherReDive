using SecondDimensionWatcherReDive.Framework.FileDownload;

namespace SecondDimensionWatcherReDive.Utils.FileDownload;

/// <summary>
///     Provides an abstract base for clients that perform torrent file download operations.
/// </summary>
public abstract class TorrentDownloadClient : IFileDownloadClient
{
    public abstract string Name { get; }
    public abstract string SupportedFileStoreType { get; }
    public string FileDownloadType => FileDownloadTypes.TorrentDownload;

    public abstract Task<bool> SubmitDownloadTaskAsync(
        Guid itemId,
        string downloadUrl,
        byte[] cachedDownloadData,
        string additionalDownloadInfo,
        CancellationToken cancellationToken);

    public abstract Task SubmitQueryDownloadProgressAsync(
        Guid itemId,
        string downloadUrl,
        byte[] cachedDownloadData,
        string additionalDownloadInfo,
        CancellationToken cancellationToken);

    public abstract Task<bool> PauseDownloadTaskAsync(
        Guid itemId,
        string downloadUrl,
        byte[] cachedDownloadData,
        string additionalDownloadInfo,
        CancellationToken cancellationToken);

    public abstract Task<bool> ResumeDownloadTaskAsync(
        Guid itemId,
        string downloadUrl,
        byte[] cachedDownloadData,
        string additionalDownloadInfo,
        CancellationToken cancellationToken);

    public abstract Task<CancelDownloadResult> CancelDownloadTaskAsync(
        Guid itemId,
        string downloadUrl,
        byte[] cachedDownloadData,
        string additionalDownloadInfo,
        bool removeFile,
        CancellationToken cancellationToken);
}