using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SecondDimensionWatcherReDive.Framework.Inference;
using SecondDimensionWatcherReDive.Inference.AI.Configuration;
using SecondDimensionWatcherReDive.Inference.AI.Tools;

namespace SecondDimensionWatcherReDive.Inference.AI.Engines;

public abstract partial class InferenceEngineBase(
    TmdbTool tmdbTool,
    IOptions<InferenceOptions> options) : IInferenceEngine
{
    protected const string SystemPrompt = """
        You are a JSON-only anime metadata extraction API. You NEVER output natural language. You NEVER explain your reasoning. Every non-tool-call response you produce MUST be exactly one raw JSON object — no prose before it, no prose after it, no markdown fences, no trailing whitespace.

        Input: a feed item title and description from an anime torrent RSS feed.

        Steps (internal — do NOT narrate these):
        1. Extract the subtitle/fansub group name (usually in brackets at the start).
        2. Extract the raw season number from the title (default to 1 if not explicit).
        3. Extract the raw episode number. Set to null for batch releases ("01-12", "Vol.1", "Complete").
        4. Call search_tmdb to find the TMDB ID.
        5. Call get_tmdb_seasons to get TMDB's season/episode structure.
           The response includes each season's actual episode_count — use these numbers, NEVER assume a fixed episode count (seasons can have 10, 12, 13, 24, 25, 48, or any number of episodes).
        6. Normalize the season and episode using the actual episode_count values:

           a) If TMDB merges multiple cours into one season (fewer TMDB seasons than the title implies):
              Compute offset = sum of episode_count for all TMDB seasons before the title's season.
              Result: TMDB season = the season containing (offset + raw_episode), episode = offset + raw_episode.
              Example: title says "S02E03", TMDB has only S01 with 48 eps → season=1, episode = S01_episode_count_before_S02 + 3.
              To calculate correctly: if TMDB S01 has 24 eps, then S02E03 → episode 24+3 = 27, still in S01 (which has 48 eps) → season=1, episode=27.

           b) If the title uses absolute numbering (e.g. "- 75"):
              Iterate TMDB seasons in order, subtracting each season's episode_count from the absolute number until it fits:
              For absolute=75: if S01 has 24 eps (75>24, remainder=51), S02 has 25 eps (51>25, remainder=26), S03 has 26 eps (26<=26) → season=3, episode=26.

           c) If the title's season and episode both exist in TMDB as-is, keep them unchanged.

           d) If uncertain about episode mapping, call get_tmdb_season_episodes for the specific season to see individual episode details (air dates, names) to verify.

        Output contract — violating this is a fatal error:
        • Exactly one JSON object, nothing else.
        • No ```json fences. No "Here is…" preamble. No explanation after the JSON.
        • Schema: {"tmdb_id":"str|null","group_name":"str|null","season":int|null,"episode":int|null}
        """;

    protected const int MaxToolRounds = 8;

    private static readonly SemaphoreSlim RateLimitSemaphore = new(1, 1);
    private static DateTime _lastCallTime = DateTime.MinValue;

    protected InferenceOptions Options => options.Value;

    protected abstract ILogger Logger { get; }

    protected abstract string ProviderName { get; }

    public async Task<InferenceResult?> InferAsync(string title, string description, CancellationToken cancellationToken)
    {
        LogStartingInference(Logger, ProviderName, title);

        await RateLimitSemaphore.WaitAsync(cancellationToken);
        try
        {
            var elapsed = DateTime.UtcNow - _lastCallTime;
            var minInterval = TimeSpan.FromMilliseconds(Options.RateLimitDelayMs);
            if (elapsed < minInterval)
            {
                var delay = minInterval - elapsed;
                LogRateLimiting(Logger, ProviderName, (int)delay.TotalMilliseconds);
                await Task.Delay(delay, cancellationToken);
            }

            var result = await InferCoreAsync(title, description, cancellationToken);
            _lastCallTime = DateTime.UtcNow;

            if (result != null)
                LogInferenceSucceeded(Logger, ProviderName, title, result.TmdbId ?? "N/A", result.Season, result.Episode);
            else
                LogInferenceNoResult(Logger, ProviderName, title);
            return result;
        }
        catch (Exception ex)
        {
            LogInferenceFailed(Logger, ex, ProviderName, title);
            return null;
        }
        finally
        {
            RateLimitSemaphore.Release();
        }
    }

    protected abstract Task<InferenceResult?> InferCoreAsync(
        string title, string description, CancellationToken cancellationToken);

    protected async Task<string> ExecuteToolCallAsync(
        string functionName, string argumentsJson, string fallbackTitle, CancellationToken cancellationToken)
    {
        if (functionName == "search_tmdb")
        {
            var args = JsonNode.Parse(argumentsJson);
            var query = args?["query"]?.GetValue<string>() ?? fallbackTitle;
            LogExecutingTmdbSearch(Logger, ProviderName, query);
            var result = await tmdbTool.SearchAsync(query, cancellationToken);
            LogTmdbSearchResult(Logger, ProviderName, result);
            return result;
        }

        if (functionName == "get_tmdb_seasons")
        {
            var args = JsonNode.Parse(argumentsJson);
            var tmdbId = args?["tmdb_id"]?.GetValue<int>() ?? 0;
            LogGettingTmdbSeasons(Logger, ProviderName, tmdbId);
            var result = await tmdbTool.GetSeasonsAsync(tmdbId, cancellationToken);
            LogTmdbSeasonsResult(Logger, ProviderName, result);
            return result;
        }

        if (functionName == "get_tmdb_season_episodes")
        {
            var args = JsonNode.Parse(argumentsJson);
            var tmdbId = args?["tmdb_id"]?.GetValue<int>() ?? 0;
            var seasonNumber = args?["season_number"]?.GetValue<int>() ?? 1;
            LogGettingTmdbSeasonEpisodes(Logger, ProviderName, tmdbId, seasonNumber);
            var result = await tmdbTool.GetSeasonEpisodesAsync(tmdbId, seasonNumber, cancellationToken);
            LogTmdbSeasonEpisodesResult(Logger, ProviderName, result);
            return result;
        }

        LogUnknownToolCall(Logger, ProviderName, functionName);
        return "{}";
    }

    protected static InferenceResult? ParseInferenceResult(string? content)
    {
        if (string.IsNullOrWhiteSpace(content)) return null;

        var jsonStr = content.Trim();

        // Strip markdown code fences
        if (jsonStr.StartsWith("```"))
        {
            var firstNewline = jsonStr.IndexOf('\n');
            if (firstNewline >= 0) jsonStr = jsonStr[(firstNewline + 1)..];
            if (jsonStr.EndsWith("```")) jsonStr = jsonStr[..^3];
            jsonStr = jsonStr.Trim();
        }

        // Try parsing the whole string as JSON first
        var json = TryParseJson(jsonStr);

        // If that fails, scan each line for a JSON object
        if (json == null)
        {
            foreach (var line in jsonStr.Split('\n'))
            {
                var trimmed = line.Trim();
                if (trimmed.StartsWith('{') && trimmed.EndsWith('}'))
                {
                    json = TryParseJson(trimmed);
                    if (json != null) break;
                }
            }
        }

        if (json == null) return null;

        return new InferenceResult(
            TmdbId: json["tmdb_id"]?.GetValue<string>(),
            GroupName: json["group_name"]?.GetValue<string>(),
            Season: json["season"]?.GetValue<int?>(),
            Episode: json["episode"]?.GetValue<int?>());
    }

    private static JsonNode? TryParseJson(string str)
    {
        try
        {
            return JsonNode.Parse(str);
        }
        catch (System.Text.Json.JsonException)
        {
            return null;
        }
    }

    protected const string SearchTmdbSchema = """
        {
            "type": "object",
            "properties": {
                "query": {
                    "type": "string",
                    "description": "The anime name to search for"
                }
            },
            "required": ["query"]
        }
        """;

    protected const string GetTmdbSeasonsSchema = """
        {
            "type": "object",
            "properties": {
                "tmdb_id": {
                    "type": "integer",
                    "description": "The TMDB TV show ID"
                }
            },
            "required": ["tmdb_id"]
        }
        """;

    protected const string GetTmdbSeasonEpisodesSchema = """
        {
            "type": "object",
            "properties": {
                "tmdb_id": {
                    "type": "integer",
                    "description": "The TMDB TV show ID"
                },
                "season_number": {
                    "type": "integer",
                    "description": "The season number to get episodes for"
                }
            },
            "required": ["tmdb_id", "season_number"]
        }
        """;

    [LoggerMessage(Level = LogLevel.Information, Message = "[{Provider}] Starting inference for title: {Title}")]
    private static partial void LogStartingInference(ILogger logger, string provider, string title);

    [LoggerMessage(Level = LogLevel.Debug, Message = "[{Provider}] Rate limiting: waiting {DelayMs}ms before next API call")]
    private static partial void LogRateLimiting(ILogger logger, string provider, int delayMs);

    [LoggerMessage(Level = LogLevel.Information, Message = "[{Provider}] Inference succeeded for title: {Title} -> TMDB: {TmdbId}, S{Season}E{Episode}")]
    private static partial void LogInferenceSucceeded(ILogger logger, string provider, string title, string tmdbId, int? season, int? episode);

    [LoggerMessage(Level = LogLevel.Warning, Message = "[{Provider}] Inference returned no result for title: {Title}")]
    private static partial void LogInferenceNoResult(ILogger logger, string provider, string title);

    [LoggerMessage(Level = LogLevel.Warning, Message = "[{Provider}] Inference failed for title: {Title}")]
    private static partial void LogInferenceFailed(ILogger logger, Exception ex, string provider, string title);

    [LoggerMessage(Level = LogLevel.Debug, Message = "[{Provider}] Executing TMDB search with query: {Query}")]
    protected static partial void LogExecutingTmdbSearch(ILogger logger, string provider, string query);

    [LoggerMessage(Level = LogLevel.Debug, Message = "[{Provider}] TMDB search returned: {Result}")]
    protected static partial void LogTmdbSearchResult(ILogger logger, string provider, string result);

    [LoggerMessage(Level = LogLevel.Debug, Message = "[{Provider}] Getting TMDB season info for ID: {TmdbId}")]
    protected static partial void LogGettingTmdbSeasons(ILogger logger, string provider, int tmdbId);

    [LoggerMessage(Level = LogLevel.Debug, Message = "[{Provider}] TMDB seasons returned: {Result}")]
    protected static partial void LogTmdbSeasonsResult(ILogger logger, string provider, string result);

    [LoggerMessage(Level = LogLevel.Warning, Message = "[{Provider}] Unknown tool call: {FunctionName}")]
    private static partial void LogUnknownToolCall(ILogger logger, string provider, string functionName);

    [LoggerMessage(Level = LogLevel.Debug, Message = "[{Provider}] Getting TMDB season episodes for ID: {TmdbId}, season: {SeasonNumber}")]
    protected static partial void LogGettingTmdbSeasonEpisodes(ILogger logger, string provider, int tmdbId, int seasonNumber);

    [LoggerMessage(Level = LogLevel.Debug, Message = "[{Provider}] TMDB season episodes returned: {Result}")]
    protected static partial void LogTmdbSeasonEpisodesResult(ILogger logger, string provider, string result);
}
