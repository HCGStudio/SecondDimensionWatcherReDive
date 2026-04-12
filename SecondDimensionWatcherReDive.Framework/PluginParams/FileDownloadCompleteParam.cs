namespace SecondDimensionWatcherReDive.Framework.PluginParams;

public class FileDownloadCompleteParam(Guid itemId, string storePath, string fileStore)
{
    public Guid ItemId { get; set; } = itemId;
    public string StorePath { get; set; } = storePath;
    public string FileStore { get; set; } = fileStore;
}