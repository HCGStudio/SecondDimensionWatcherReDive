namespace SecondDimensionWatcherReDive.Framework.Inference;

/// <summary>
///     Represents the result of AI inference on a feed item.
///     Name, original name, and description are fetched from TMDB directly.
/// </summary>
public record InferenceResult(
    string? TmdbId,
    string? GroupName,
    int? Season,
    int? Episode);
