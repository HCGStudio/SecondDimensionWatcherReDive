namespace SecondDimensionWatcherReDive.Data;

public record DownloadCompleteRequest(Guid ItemId, string StorePath, string FileStore);