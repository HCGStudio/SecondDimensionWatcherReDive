namespace SecondDimensionWatcherReDive.Data;

public record struct DownloadCompleteRequest(Guid ItemId, string StorePath, string FileStore);