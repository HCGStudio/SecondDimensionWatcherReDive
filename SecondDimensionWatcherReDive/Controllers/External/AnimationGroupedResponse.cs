namespace SecondDimensionWatcherReDive.Controllers.External;

internal sealed record AnimationGroupedResponse(
    IReadOnlyList<AnimationWithEpisodes> Animations,
    IReadOnlyList<AnimationInfo> Uncategorized);

internal sealed record AnimationWithEpisodes(
    string TmdbId,
    string Name,
    string OriginalName,
    string? PosterPath,
    int EpisodeCount,
    IReadOnlyList<AnimationInfo> Episodes);
