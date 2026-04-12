using System.Text.Json;
using Microsoft.Extensions.Logging;
using TMDbLib.Client;

namespace SecondDimensionWatcherReDive.Inference.AI.Tools;

public class TmdbTool(TMDbClient tmdbClient, ILogger<TmdbTool> logger)
{
    public async Task<string> SearchAsync(string query, CancellationToken cancellationToken)
    {
        logger.LogDebug("[TMDB] Searching for: {Query}", query);
        try
        {
            var tvResults = await tmdbClient.SearchTvShowAsync(query, cancellationToken: cancellationToken);

            if (tvResults?.Results is { Count: > 0 })
            {
                logger.LogDebug("[TMDB] Found {Count} TV results for: {Query}", tvResults.Results.Count, query);
                var results = tvResults.Results.Take(5).Select(r => new
                {
                    tmdb_id = r.Id.ToString(),
                    name = r.Name,
                    original_name = r.OriginalName,
                    first_air_date = r.FirstAirDate?.ToString("yyyy-MM-dd"),
                    media_type = "tv"
                });
                return JsonSerializer.Serialize(results);
            }

            var movieResults = await tmdbClient.SearchMovieAsync(query, cancellationToken: cancellationToken);

            if (movieResults?.Results is { Count: > 0 })
            {
                logger.LogDebug("[TMDB] Found {Count} movie results for: {Query}", movieResults.Results.Count, query);
                var results = movieResults.Results.Take(5).Select(r => new
                {
                    tmdb_id = r.Id.ToString(),
                    name = r.Title,
                    original_name = r.OriginalTitle,
                    release_date = r.ReleaseDate?.ToString("yyyy-MM-dd"),
                    media_type = "movie"
                });
                return JsonSerializer.Serialize(results);
            }

            logger.LogDebug("[TMDB] No results found for: {Query}", query);
            return "[]";
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "TMDB search failed for query: {Query}", query);
            return "[]";
        }
    }
}
