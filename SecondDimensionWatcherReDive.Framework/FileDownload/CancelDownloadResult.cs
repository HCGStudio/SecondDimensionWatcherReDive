namespace SecondDimensionWatcherReDive.Framework.FileDownload;

public record struct CancelDownloadResult(bool IsSuccess, bool NeedRemoveFromFileStore);