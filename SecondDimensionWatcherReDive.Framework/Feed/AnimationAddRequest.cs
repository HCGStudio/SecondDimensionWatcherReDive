namespace SecondDimensionWatcherReDive.Framework.Feed;

public record AnimationAddRequest(
    DateTimeOffset PublishTime,
    string Title,
    string Description,
    string DownloadUrl,
    string DownloadType,
    string AdditionalDownloadInfo,
    Guid? FeedId = null,
    long? ContentLength = null,
    string? FeedItemGuid = null,
    string? EnclosureId = null);
