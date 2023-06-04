using SecondDimensionWatcherReDive.Models;

namespace SecondDimensionWatcherReDive.Data;

public record AnimationAddRequest(
    DateTimeOffset PublishTime,
    string Title,
    string Description,
    string DownloadUrl,
    string DownloadType,
    string AdditionalDownloadInfo,
    AnimationGroupDto? Group,
    AnimationDto? Animation);