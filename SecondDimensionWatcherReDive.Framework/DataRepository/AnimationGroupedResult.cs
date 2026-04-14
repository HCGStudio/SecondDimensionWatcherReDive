namespace SecondDimensionWatcherReDive.Framework.DataRepository;

public sealed record AnimationGroupedResult(
    IReadOnlyList<AnimationWithEpisodesResult> Animations,
    IReadOnlyList<AnimationInfo> Uncategorized);

public sealed record AnimationWithEpisodesResult(
    string TmdbId,
    string Name,
    string OriginalName,
    string? PosterPath,
    int EpisodeCount,
    IReadOnlyList<AnimationInfo> Episodes);
