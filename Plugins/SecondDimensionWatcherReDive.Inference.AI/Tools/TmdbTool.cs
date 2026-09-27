using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using TMDbLib.Client;
using TMDbLib.Objects.Exceptions;

namespace SecondDimensionWatcherReDive.Inference.AI.Tools;

public partial class TmdbTool
{
    private readonly IConfiguration? _configuration;
    private readonly bool _explicitlyConfigured;
    private readonly ILogger<TmdbTool> _logger;
    private readonly object _clientLock = new();
    private TMDbClient? _tmdbClient;
    private string? _cachedApiKey;

    public TmdbTool(IConfiguration configuration, ILogger<TmdbTool> logger)
    {
        _configuration = configuration;
        _logger = logger;
    }

    public TmdbTool(TMDbClient tmdbClient, ILogger<TmdbTool> logger)
        : this(tmdbClient, logger, true)
    {
    }

    public TmdbTool(TMDbClient tmdbClient, ILogger<TmdbTool> logger, bool isConfigured)
    {
        _tmdbClient = tmdbClient;
        _tmdbClient.ThrowApiExceptions = true;
        _logger = logger;
        _explicitlyConfigured = isConfigured;
    }

    public bool IsConfigured => _configuration is not null
        ? !string.IsNullOrWhiteSpace(_configuration["TmdbApiKey"])
        : _explicitlyConfigured;

    public async Task<string> SearchAsync(string query, CancellationToken cancellationToken)
    {
        var tmdbClient = GetClient();
        if (tmdbClient is null) return "[]";

        LogSearching(_logger, query);
        try
        {
            var tvResults = await tmdbClient.SearchTvShowAsync(query, cancellationToken: cancellationToken);

            if (tvResults?.Results is { Count: > 0 })
            {
                LogFoundTvResults(_logger, tvResults.Results.Count, query);
                var results = tvResults.Results.Take(5).Select(r => new TmdbTvSearchResult(
                    r.Id.ToString(), r.Name, r.OriginalName, r.FirstAirDate?.ToString("yyyy-MM-dd"),
                    r.Overview, "tv")).ToArray();
                return JsonSerializer.Serialize(results, InferenceToolJsonContext.Default.TmdbTvSearchResultArray);
            }

            var movieResults = await tmdbClient.SearchMovieAsync(query, cancellationToken: cancellationToken);

            if (movieResults?.Results is { Count: > 0 })
            {
                LogFoundMovieResults(_logger, movieResults.Results.Count, query);
                var results = movieResults.Results.Take(5).Select(r => new TmdbMovieSearchResult(
                    r.Id.ToString(), r.Title, r.OriginalTitle, r.ReleaseDate?.ToString("yyyy-MM-dd"),
                    r.Overview, "movie")).ToArray();
                return JsonSerializer.Serialize(results, InferenceToolJsonContext.Default.TmdbMovieSearchResultArray);
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
        var tmdbClient = GetClient();
        if (tmdbClient is null) return "{}";

        LogGettingSeasonInfo(_logger, tmdbId);
        try
        {
            var show = await tmdbClient.GetTvShowAsync(tmdbId, cancellationToken: cancellationToken);
            if (show == null)
            {
                LogTvShowNotFound(_logger, tmdbId);
                return "{}";
            }

            var seasons = show.Seasons?
                .Where(s => s.SeasonNumber > 0) // exclude specials (season 0)
                .Select(s => new TmdbSeasonSummary(
                    s.SeasonNumber, s.EpisodeCount, s.Name, s.AirDate?.ToString("yyyy-MM-dd")))
                .ToList() ?? [];

            var result = new TmdbSeasonsResult(show.Id, show.Name, show.OriginalName, seasons.Count, seasons);

            LogShowSeasonCount(_logger, tmdbId, seasons.Count);
            return JsonSerializer.Serialize(result, InferenceToolJsonContext.Default.TmdbSeasonsResult);
        }
        catch (Exception ex)
        {
            LogGetSeasonsFailed(_logger, ex, tmdbId);
            return "{}";
        }
    }

    public async Task<string> GetSeasonEpisodesAsync(int tmdbId, int seasonNumber, CancellationToken cancellationToken)
    {
        var tmdbClient = GetClient();
        if (tmdbClient is null) return "{}";

        LogGettingSeasonEpisodes(_logger, tmdbId, seasonNumber);
        try
        {
            var season = await tmdbClient.GetTvSeasonAsync(tmdbId, seasonNumber,
                cancellationToken: cancellationToken);
            if (season == null)
            {
                LogSeasonNotFound(_logger, tmdbId, seasonNumber);
                return "{}";
            }

            var episodes = season.Episodes?
                .Select(e => new TmdbEpisodeSummary(
                    e.EpisodeNumber, e.Name, e.AirDate?.ToString("yyyy-MM-dd"), e.Overview))
                .ToList() ?? [];

            var result = new TmdbSeasonEpisodesResult(tmdbId, seasonNumber, episodes.Count, episodes);

            LogSeasonEpisodeCount(_logger, tmdbId, seasonNumber, episodes.Count);
            return JsonSerializer.Serialize(result, InferenceToolJsonContext.Default.TmdbSeasonEpisodesResult);
        }
        catch (Exception ex)
        {
            LogGetSeasonEpisodesFailed(_logger, ex, tmdbId, seasonNumber);
            return "{}";
        }
    }

    public async Task<int?> GetExpectedEpisodeCountAsync(
        int tmdbId,
        int seasonNumber,
        CancellationToken cancellationToken)
    {
        var tmdbClient = GetClient();
        if (tmdbClient is null || seasonNumber <= 0) return null;

        try
        {
            var show = await tmdbClient.GetTvShowAsync(tmdbId, cancellationToken: cancellationToken);
            var count = show?.Seasons?
                .FirstOrDefault(season => season.SeasonNumber == seasonNumber)
                ?.EpisodeCount;
            return count is > 0 ? count : null;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            LogGetSeasonsFailed(_logger, ex, tmdbId);
            return null;
        }
    }

    /// <summary>
    ///     Fetches localized name, original name, and overview for a TV show from TMDB,
    ///     using the server's current culture as the language.
    /// </summary>
    public async Task<TmdbDetails?> GetLocalizedDetailsAsync(int tmdbId, CancellationToken cancellationToken) =>
        (await LookupLocalizedDetailsAsync(tmdbId, cancellationToken)).Details;

    /// <summary>
    ///     Retains the lookup outcome for callers that must distinguish a missing target
    ///     from an unavailable API. The nullable compatibility API above keeps its behavior.
    /// </summary>
    public async Task<TmdbDetailsLookup> LookupLocalizedDetailsAsync(int tmdbId, CancellationToken cancellationToken)
    {
        var tmdbClient = GetClient();
        if (tmdbClient is null) return new(TmdbDetailsLookupStatus.Unavailable, null);

        var language = CultureInfo.CurrentCulture.Name;
        LogGettingLocalizedDetails(_logger, tmdbId, language);
        try
        {
            var show = await tmdbClient.GetTvShowAsync(tmdbId, language: language,
                cancellationToken: cancellationToken);
            // With ThrowApiExceptions enabled, a real HTTP 404 throws NotFoundException.
            // A successful response with no deserializable show is not evidence of absence.
            if (show is null) return new(TmdbDetailsLookupStatus.Unavailable, null);

            return new(TmdbDetailsLookupStatus.Found, new TmdbDetails(
                Name: show.Name ?? "",
                OriginalName: show.OriginalName ?? "",
                Overview: show.Overview,
                PosterPath: show.PosterPath),
                // Keep specials (season 0) and distinguish absent season data
                // from a confirmed empty list for strict target validation.
                show.Seasons?.Select(season => season.SeasonNumber).Distinct().ToArray());
        }
        catch (NotFoundException)
        {
            return new(TmdbDetailsLookupStatus.NotFound, null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            LogGetLocalizedDetailsFailed(_logger, ex, tmdbId);
            return new(TmdbDetailsLookupStatus.Unavailable, null);
        }
    }

    private TMDbClient? GetClient()
    {
        if (_configuration is null)
            return _explicitlyConfigured ? _tmdbClient : null;

        var apiKey = _configuration["TmdbApiKey"];
        if (string.IsNullOrWhiteSpace(apiKey)) return null;

        lock (_clientLock)
        {
            if (_tmdbClient is null || !string.Equals(_cachedApiKey, apiKey, StringComparison.Ordinal))
            {
                _tmdbClient = new TMDbClient(apiKey) { ThrowApiExceptions = true };
                _cachedApiKey = apiKey;
            }

            return _tmdbClient;
        }
    }

    public enum TmdbDetailsLookupStatus { Found, NotFound, Unavailable }

    public sealed record TmdbDetailsLookup(TmdbDetailsLookupStatus Status, TmdbDetails? Details,
        IReadOnlyList<int>? SeasonNumbers = null);

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

internal sealed record TmdbTvSearchResult(
    string TmdbId, string? Name, string? OriginalName, string? FirstAirDate, string? Overview, string MediaType);
internal sealed record TmdbMovieSearchResult(
    string TmdbId, string? Name, string? OriginalName, string? ReleaseDate, string? Overview, string MediaType);
internal sealed record TmdbSeasonSummary(int SeasonNumber, int EpisodeCount, string? Name, string? AirDate);
internal sealed record TmdbSeasonsResult(
    int TmdbId, string? Name, string? OriginalName, int TotalSeasons, List<TmdbSeasonSummary> Seasons);
internal sealed record TmdbEpisodeSummary(long EpisodeNumber, string? Name, string? AirDate, string? Overview);
internal sealed record TmdbSeasonEpisodesResult(
    int TmdbId, int SeasonNumber, int EpisodeCount, List<TmdbEpisodeSummary> Episodes);
