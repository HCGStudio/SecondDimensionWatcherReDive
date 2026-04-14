namespace SecondDimensionWatcherReDive.Framework.DataRepository;

public sealed record Animation(
    Guid Id,
    string TmdbId,
    string Name,
    string OriginalName,
    string? PosterPath);
