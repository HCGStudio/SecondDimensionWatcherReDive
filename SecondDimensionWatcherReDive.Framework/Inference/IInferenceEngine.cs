namespace SecondDimensionWatcherReDive.Framework.Inference;

/// <summary>
///     Interface for AI inference engines that extract structured anime metadata
///     from feed titles and descriptions.
/// </summary>
public interface IInferenceEngine
{
    /// <summary>
    ///     Infers animation metadata from a single feed item's title and description.
    /// </summary>
    /// <param name="title">The feed item title (e.g., "[SubGroup] Anime Name - 05 [1080p]").</param>
    /// <param name="description">The feed item description, may contain additional metadata.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Structured inference result, or null if inference fails entirely.</returns>
    Task<InferenceResult?> InferAsync(string title, string description, CancellationToken cancellationToken);
}
