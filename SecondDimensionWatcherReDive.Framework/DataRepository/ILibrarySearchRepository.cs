namespace SecondDimensionWatcherReDive.Framework.DataRepository;

public interface ILibrarySearchRepository
{
    Task<LibrarySearchResult> SearchAsync(
        LibrarySearchRequest request,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<LibraryIntegritySummary>> GetIntegrityAsync(
        string? tmdbId,
        int? season,
        CancellationToken cancellationToken);
}
