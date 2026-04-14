namespace SecondDimensionWatcherReDive.Controllers.External;

internal sealed record AnimationInfo(
    Guid Id,
    string Title,
    string Description,
    DateTimeOffset PublishTime,
    bool IsDownloadTracked,
    bool IsDownloadFinished,
    int? Season,
    int? Episode,
    AnimationGroup? Group,
    Animation? Animation,
    bool IsAiProcessed);

internal sealed record Animation(
    string Name,
    string OriginalName,
    string TmdbId,
    string? PosterPath);

internal sealed record AnimationGroup(string Name);
