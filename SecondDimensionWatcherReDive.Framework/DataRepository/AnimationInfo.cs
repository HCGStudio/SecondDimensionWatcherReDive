namespace SecondDimensionWatcherReDive.Framework.DataRepository;

public sealed record AnimationInfo(
    Guid Id,
    string Title,
    string Description,
    DateTimeOffset PublishTime,
    string DownloadUrl,
    string DownloadType,
    byte[] CachedDownloadData,
    string AdditionalDownloadInfo,
    bool IsDownloadTracked,
    DateTimeOffset DownloadStartTime,
    DateTimeOffset DownloadEndTime,
    bool IsDownloadFinished,
    string? FileStore,
    string? StorePath,
    int? Season,
    int? Episode,
    AnimationGroup? Group,
    Animation? Animation,
    bool IsAiProcessed,
    int AiRetryCount);
