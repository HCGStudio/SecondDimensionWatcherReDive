using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using TMDbLib.Client;

namespace SecondDimensionWatcherReDive.Inference.AI.Tools;

public partial class TmdbTool
{
    private readonly bool _isConfigured;
    private readonly ILogger<TmdbTool> _logger;
    private readonly TMDbClient _tmdbClient;

    public TmdbTool(TMDbClient tmdbClient, ILogger<TmdbTool> logger)
        : this(tmdbClient, logger, true)
    {
    }

    public TmdbTool(TMDbClient tmdbClient, ILogger<TmdbTool> logger, bool isConfigured)
    {
        _tmdbClient = tmdbClient;
        _logger = logger;
        _isConfigured = isConfigured;
    }

    public bool IsConfigured => _isConfigured;

    public async Task<string> SearchAsync(string query, CancellationToken cancellationToken)
    {
        if (!_isConfigured) return "[]";

        LogSearching(_logger, query);
        try
        {
            var tvResults = await _tmdbClient.SearchTvShowAsync(query, cancellationToken: cancellationToken);

            if (tvResults?.Results is { Count: > 0 })
            {
                LogFoundTvResults(_logger, tvResults.Results.Count, query);
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

            var movieResults = await _tmdbClient.SearchMovieAsync(query, cancellationToken: cancellationToken);

            if (movieResults?.Results is { Count: > 0 })
            {
                LogFoundMovieResults(_logger, movieResults.Results.Count, query);
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

            LogNoResultsFound(_logger, query);
            return "[]";
        }
        catch (Exception ex)
        {
            LogSearchFailed(_logger, ex, query);
            return "[]";
        }
    }

    public async Task<string> GetSeasonsAsync(int tmdbId, CancellationToken cancellationToken)
    {
        if (!_isConfigured) return "{}";

        LogGettingSeasonInfo(_logger, tmdbId);
        try
        {
            var show = await _tmdbClient.GetTvShowAsync(tmdbId, cancellationToken: cancellationToken);
            if (show == null)
            {
                LogTvShowNotFound(_logger, tmdbId);
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

            LogShowSeasonCount(_logger, tmdbId, seasons.Count);
            return JsonSerializer.Serialize(result);
        }
        catch (Exception ex)
        {
            LogGetSeasonsFailed(_logger, ex, tmdbId);
            return "{}";
        }
    }

    public async Task<string> GetSeasonEpisodesAsync(int tmdbId, int seasonNumber, CancellationToken cancellationToken)
    {
        if (!_isConfigured) return "{}";

        LogGettingSeasonEpisodes(_logger, tmdbId, seasonNumber);
        try
        {
            var season = await _tmdbClient.GetTvSeasonAsync(tmdbId, seasonNumber, cancellationToken: cancellationToken);
            if (season == null)
            {
                LogSeasonNotFound(_logger, tmdbId, seasonNumber);
                return "{}";
            }

            var episodes = season.Episodes?
                .Select(e => new
                {
                    episode_number = e.EpisodeNumber,
                    name = e.Name,
                    air_date = e.AirDate?.ToString("yyyy-MM-dd"),
                    overview = e.Overview
                })
                .ToList() ?? [];

            var result = new
            {
                tmdb_id = tmdbId,
                season_number = seasonNumber,
                episode_count = episodes.Count,
                episodes
            };

            LogSeasonEpisodeCount(_logger, tmdbId, seasonNumber, episodes.Count);
            return JsonSerializer.Serialize(result);
        }
        catch (Exception ex)
        {
            LogGetSeasonEpisodesFailed(_logger, ex, tmdbId, seasonNumber);
            return "{}";
        }
    }

    /// <summary>
    ///     Fetches localized name, original name, and overview for a TV show from TMDB,
    ///     using the server's current culture as the language.
    /// </summary>
    public async Task<TmdbDetails?> GetLocalizedDetailsAsync(int tmdbId, CancellationToken cancellationToken)
    {
        if (!_isConfigured) return null;

        var language = CultureInfo.CurrentCulture.Name; // e.g. "zh-CN", "en-US", "ja-JP"
        LogGettingLocalizedDetails(_logger, tmdbId, language);
        try
        {
            var show = await _tmdbClient.GetTvShowAsync(tmdbId, language: language,
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
            LogGetLocalizedDetailsFailed(_logger, ex, tmdbId);
            return null;
        }
    }

    public record TmdbDetails(string Name, string OriginalName, string? Overview, string? PosterPath);

    [LoggerMessage(Level = LogLevel.Debug, Message = "[TMDB] Searching for: {Query}")]
    private static partial void LogSearching(ILogger logger, string query);

    [LoggerMessage(Level = LogLevel.Debug, Message = "[TMDB] Found {Count} TV results for: {Query}")]
    private static partial void LogFoundTvResults(ILogger logger, int count, string query);

    [LoggerMessage(Level = LogLevel.Debug, Message = "[TMDB] Found {Count} movie results for: {Query}")]
    private static partial void LogFoundMovieResults(ILogger logger, int count, string query);

    [LoggerMessage(Level = LogLevel.Debug, Message = "[TMDB] No results found for: {Query}")]
    private static partial void LogNoResultsFound(ILogger logger, string query);

    [LoggerMessage(Level = LogLevel.Warning, Message = "TMDB search failed for query: {Query}")]
    private static partial void LogSearchFailed(ILogger logger, Exception ex, string query);

    [LoggerMessage(Level = LogLevel.Debug, Message = "[TMDB] Getting season info for TV show: {TmdbId}")]
    private static partial void LogGettingSeasonInfo(ILogger logger, int tmdbId);

    [LoggerMessage(Level = LogLevel.Debug, Message = "[TMDB] TV show not found: {TmdbId}")]
    private static partial void LogTvShowNotFound(ILogger logger, int tmdbId);

    [LoggerMessage(Level = LogLevel.Debug, Message = "[TMDB] Show {TmdbId} has {SeasonCount} seasons")]
    private static partial void LogShowSeasonCount(ILogger logger, int tmdbId, int seasonCount);

    [LoggerMessage(Level = LogLevel.Warning, Message = "TMDB GetSeasons failed for ID: {TmdbId}")]
    private static partial void LogGetSeasonsFailed(ILogger logger, Exception ex, int tmdbId);

    [LoggerMessage(Level = LogLevel.Debug, Message = "[TMDB] Getting localized details for {TmdbId} in {Language}")]
    private static partial void LogGettingLocalizedDetails(ILogger logger, int tmdbId, string language);

    [LoggerMessage(Level = LogLevel.Warning, Message = "TMDB GetLocalizedDetails failed for ID: {TmdbId}")]
    private static partial void LogGetLocalizedDetailsFailed(ILogger logger, Exception ex, int tmdbId);

    [LoggerMessage(Level = LogLevel.Debug, Message = "[TMDB] Getting episodes for show {TmdbId} season {SeasonNumber}")]
    private static partial void LogGettingSeasonEpisodes(ILogger logger, int tmdbId, int seasonNumber);

    [LoggerMessage(Level = LogLevel.Debug, Message = "[TMDB] Season not found: show {TmdbId} season {SeasonNumber}")]
    private static partial void LogSeasonNotFound(ILogger logger, int tmdbId, int seasonNumber);

    [LoggerMessage(Level = LogLevel.Debug, Message = "[TMDB] Show {TmdbId} season {SeasonNumber} has {EpisodeCount} episodes")]
    private static partial void LogSeasonEpisodeCount(ILogger logger, int tmdbId, int seasonNumber, int episodeCount);

    [LoggerMessage(Level = LogLevel.Warning, Message = "TMDB GetSeasonEpisodes failed for show {TmdbId} season {SeasonNumber}")]
    private static partial void LogGetSeasonEpisodesFailed(ILogger logger, Exception ex, int tmdbId, int seasonNumber);
}
