namespace SecondDimensionWatcherReDive.Controllers.External;

internal sealed record AnimationCatalogResponse(
    IReadOnlyList<AnimationCatalogItem> Items,
    string? NextCursor);

internal sealed record AnimationInfoSummaryResponse(
    IReadOnlyList<AnimationInfo> Items,
    string? NextCursor);

internal sealed record AnimationEpisodeResponse(
    AnimationCatalogItem Animation,
    IReadOnlyList<AnimationInfo> Episodes,
    string? NextCursor);

internal sealed record AnimationCatalogItem(
    string TmdbId,
    string Name,
    string OriginalName,
    string? PosterPath,
    int EpisodeCount,
    int ReleaseCount,
    int AutomationAttentionCount,
    DateTimeOffset LatestPublishTime);
