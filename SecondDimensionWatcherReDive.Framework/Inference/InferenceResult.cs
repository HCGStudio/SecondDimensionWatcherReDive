namespace SecondDimensionWatcherReDive.Framework.Inference;

/// <summary>
///     Represents the result of AI inference on a feed item.
/// </summary>
public record InferenceResult(
    string AnimationName,
    string OriginalName,
    string? TmdbId,
    string? GroupName,
    int? Season,
    int? Episode);
