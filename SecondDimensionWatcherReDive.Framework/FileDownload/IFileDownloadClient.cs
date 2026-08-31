namespace SecondDimensionWatcherReDive.Framework.FileDownload;

public enum DownloadTaskReconciliationOutcome
{
    Confirmed,
    Rejected,
    Unknown
}

/// <summary>
///     Provides a contract for performing various file download operations.
/// </summary>
public interface IFileDownloadClient
{
    /// <summary>
    ///     Gets the name of the file download client.
    /// </summary>
    public string Name { get; }

    /// <summary>
    ///     Gets the type of file store supported by the client.
    /// </summary>
    public string SupportedFileStoreType { get; }

    /// <summary>
    ///     Gets the type of file download action performed by the client.
    /// </summary>
    public string FileDownloadType { get; }

    /// <summary>
    ///     Submits a download task.
    /// </summary>
    /// <param name="itemId">Identifier of the item to download.</param>
    /// <param name="downloadUrl">URL from which to download the item.</param>
    /// <param name="cachedDownloadData">Cache data for the download.</param>
    /// <param name="additionalDownloadInfo">Additional information for the download.</param>
    /// <returns>Whether the download client accepted the task.</returns>
    public Task<bool> SubmitDownloadTaskAsync(
        Guid itemId,
        string downloadUrl,
        byte[] cachedDownloadData,
        string additionalDownloadInfo,
        CancellationToken cancellationToken);

    /// <summary>
    ///     Ensures a previously persisted download submission exists remotely.
    ///     Implementations must make this operation idempotent for the same item.
    /// </summary>
    /// <returns>The confirmed, rejected, or still-uncertain reconciliation outcome.</returns>
    public Task<DownloadTaskReconciliationOutcome> EnsureDownloadTaskAsync(
        Guid itemId,
        string downloadUrl,
        byte[] cachedDownloadData,
        string additionalDownloadInfo,
        CancellationToken cancellationToken);

    /// <summary>
    ///     Submits a query to check the progress of a download.
    /// </summary>
    /// <param name="itemId">Identifier of the item being downloaded.</param>
    /// <param name="downloadUrl">URL from which the item is being downloaded.</param>
    /// <param name="cachedDownloadData">Cached data for the download.</param>
    /// <param name="additionalDownloadInfo">Additional information for the download.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    public Task SubmitQueryDownloadProgressAsync(
        Guid itemId,
        string downloadUrl,
        byte[] cachedDownloadData,
        string additionalDownloadInfo,
        CancellationToken cancellationToken);

    /// <summary>
    ///     Pauses a download task.
    /// </summary>
    /// <param name="itemId">Identifier of the item being downloaded.</param>
    /// <param name="downloadUrl">URL from which the item is being downloaded.</param>
    /// <param name="cachedDownloadData">Cached data for the download.</param>
    /// <param name="additionalDownloadInfo">Additional information for the download.</param>
    /// <returns>
    ///     A task that represents the asynchronous operation. The task result contains a boolean indicating if the
    ///     download task was paused successfully.
    /// </returns>
    public Task<bool> PauseDownloadTaskAsync(
        Guid itemId,
        string downloadUrl,
        byte[] cachedDownloadData,
        string additionalDownloadInfo,
        CancellationToken cancellationToken);

    /// <summary>
    ///     Resumes a paused download task.
    /// </summary>
    /// <param name="itemId">Identifier of the item being downloaded.</param>
    /// <param name="downloadUrl">URL from which the item is being downloaded.</param>
    /// <param name="cachedDownloadData">Cached data for the download.</param>
    /// <param name="additionalDownloadInfo">Additional information for the download.</param>
    /// <returns>
    ///     A task that represents the asynchronous operation. The task result contains a boolean indicating if the
    ///     download task was resumed successfully.
    /// </returns>
    public Task<bool> ResumeDownloadTaskAsync(
        Guid itemId,
        string downloadUrl,
        byte[] cachedDownloadData,
        string additionalDownloadInfo,
        CancellationToken cancellationToken);

    /// <summary>
    ///     Cancels a download task.
    /// </summary>
    /// <param name="itemId">Identifier of the item being downloaded.</param>
    /// <param name="downloadUrl">URL from which the item is being downloaded.</param>
    /// <param name="cachedDownloadData">Cached data for the download.</param>
    /// <param name="additionalDownloadInfo">Additional information for the download.</param>
    /// <param name="removeFile">True to remove the downloaded file from the file store, false otherwise.</param>
    /// <returns>
    ///     A task that represents the asynchronous operation. The task result contains a CancelDownloadResult object
    ///     that indicates whether the cancellation was successful and if the file needs to be removed from the file store.
    /// </returns>
    public Task<CancelDownloadResult> CancelDownloadTaskAsync(
        Guid itemId,
        string downloadUrl,
        byte[] cachedDownloadData,
        string additionalDownloadInfo,
        bool removeFile,
        CancellationToken cancellationToken);
}
