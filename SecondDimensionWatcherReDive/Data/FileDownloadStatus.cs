namespace SecondDimensionWatcherReDive.Data;

public sealed record FileDownloadStatus(
    Guid ItemId,
    double Progress,
    TimeSpan Remaining,
    int Speed,
    FileDownloadState State);
