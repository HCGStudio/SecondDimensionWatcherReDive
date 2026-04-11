namespace SecondDimensionWatcherReDive.Framework.Feed;

public record AnimationAddRequest(
    DateTimeOffset PublishTime,
    string Title,
    string Description,
    string DownloadUrl,
    string DownloadType,
    string AdditionalDownloadInfo);