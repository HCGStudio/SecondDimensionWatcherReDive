namespace SecondDimensionWatcherReDive.Utils.FileDownload;

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
    /// <returns>
    ///     A task that represents the asynchronous operation. The task result contains a boolean that indicates if the
    ///     download task was submitted successfully.
    /// </returns>
    public Task<bool> SubmitDownloadTask(
        Guid itemId,
        string downloadUrl,
        byte[] cachedDownloadData,
        string additionalDownloadInfo);

    /// <summary>
    ///     Submits a query to check the progress of a download.
    /// </summary>
    /// <param name="itemId">Identifier of the item being downloaded.</param>
    /// <param name="downloadUrl">URL from which the item is being downloaded.</param>
    /// <param name="cachedDownloadData">Cached data for the download.</param>
    /// <param name="additionalDownloadInfo">Additional information for the download.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    public Task SubmitQueryDownloadProgress(
        Guid itemId,
        string downloadUrl,
        byte[] cachedDownloadData,
        string additionalDownloadInfo);

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
    public Task<bool> PauseDownloadTask(
        Guid itemId,
        string downloadUrl,
        byte[] cachedDownloadData,
        string additionalDownloadInfo);

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
    public Task<bool> ResumeDownloadTask(
        Guid itemId,
        string downloadUrl,
        byte[] cachedDownloadData,
        string additionalDownloadInfo);
}