namespace SecondDimensionWatcherReDive.Framework.PluginParams;

public class FileDownloadCompleteParam(
    Guid itemId,
    string storePath,
    string fileStore,
    Guid? eventId = null)
{
    /// <summary>
    /// Stable identifier for this completion workflow. Plugin handlers can persist
    /// it as an idempotency key before performing externally visible work.
    /// </summary>
    public Guid EventId { get; } = eventId ?? itemId;

    public Guid ItemId { get; set; } = itemId;
    public string StorePath { get; set; } = storePath;
    public string FileStore { get; set; } = fileStore;
}
