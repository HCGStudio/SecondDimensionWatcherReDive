using System.Globalization;
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
                    overview = r.Overview,
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
                    overview = r.Overview,
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

    public async Task<string> GetSeasonsAsync(int tmdbId, CancellationToken cancellationToken)
    {
        logger.LogDebug("[TMDB] Getting season info for TV show: {TmdbId}", tmdbId);
        try
        {
            var show = await tmdbClient.GetTvShowAsync(tmdbId, cancellationToken: cancellationToken);
            if (show == null)
            {
                logger.LogDebug("[TMDB] TV show not found: {TmdbId}", tmdbId);
                return "{}";
            }

            var seasons = show.Seasons?
                .Where(s => s.SeasonNumber > 0) // exclude specials (season 0)
                .Select(s => new
                {
                    season_number = s.SeasonNumber,
                    episode_count = s.EpisodeCount,
                    name = s.Name,
                    air_date = s.AirDate?.ToString("yyyy-MM-dd")
                })
                .ToList() ?? [];

            var result = new
            {
                tmdb_id = show.Id,
                name = show.Name,
                original_name = show.OriginalName,
                total_seasons = seasons.Count,
                seasons
            };

            logger.LogDebug("[TMDB] Show {TmdbId} has {SeasonCount} seasons", tmdbId, seasons.Count);
            return JsonSerializer.Serialize(result);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "TMDB GetSeasons failed for ID: {TmdbId}", tmdbId);
            return "{}";
        }
    }

    /// <summary>
    ///     Fetches localized name, original name, and overview for a TV show from TMDB,
    ///     using the server's current culture as the language.
    /// </summary>
    public async Task<TmdbDetails?> GetLocalizedDetailsAsync(int tmdbId, CancellationToken cancellationToken)
    {
        var language = CultureInfo.CurrentCulture.Name; // e.g. "zh-CN", "en-US", "ja-JP"
        logger.LogDebug("[TMDB] Getting localized details for {TmdbId} in {Language}", tmdbId, language);
        try
        {
            var show = await tmdbClient.GetTvShowAsync(tmdbId, language: language,
                cancellationToken: cancellationToken);
            if (show == null) return null;

            return new TmdbDetails(
                Name: show.Name ?? "",
                OriginalName: show.OriginalName ?? "",
                Overview: show.Overview,
                PosterPath: show.PosterPath);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "TMDB GetLocalizedDetails failed for ID: {TmdbId}", tmdbId);
            return null;
        }
    }

    public record TmdbDetails(string Name, string OriginalName, string? Overview, string? PosterPath);
}
