namespace SecondDimensionWatcherReDive.Utils.FileDownload;

/// <summary>
///     Defines a contract for providing clients to download files.
/// </summary>
public interface IFileDownloadClientProvider
{
    /// <summary>
    ///     Retrieves the required client for a given download type.
    /// </summary>
    /// <param name="downloadType">The type of the download.</param>
    /// <returns>An instance of IFileDownloadClient for the specified download type.</returns>
    public IFileDownloadClient GetRequiredClient(string downloadType);

    /// <summary>
    ///     Attempts to retrieve the client for a given download type.
    /// </summary>
    /// <param name="downloadType">The type of the download.</param>
    /// <returns>An instance of IFileDownloadClient if found for the specified download type, otherwise null.</returns>
    public IFileDownloadClient? GetClient(string downloadType);

    /// <summary>
    ///     Retrieves the required client for a given download type and file store type.
    /// </summary>
    /// <param name="downloadType">The type of the download.</param>
    /// <param name="fileStoreType">The type of the file store.</param>
    /// <returns>An instance of IFileDownloadClient for the specified download and file store types.</returns>
    public IFileDownloadClient GetRequiredClient(string downloadType, string fileStoreType);

    /// <summary>
    ///     Attempts to retrieve the client for a given download type and file store type.
    /// </summary>
    /// <param name="downloadType">The type of the download.</param>
    /// <param name="fileStoreType">The type of the file store.</param>
    /// <returns>An instance of IFileDownloadClient if found for the specified download and file store types, otherwise null.</returns>
    public IFileDownloadClient? GetClient(string downloadType, string fileStoreType);
}