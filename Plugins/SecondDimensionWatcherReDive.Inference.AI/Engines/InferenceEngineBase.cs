using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SecondDimensionWatcherReDive.Framework.Inference;
using SecondDimensionWatcherReDive.Inference.AI.Configuration;
using SecondDimensionWatcherReDive.Inference.AI.Tools;

namespace SecondDimensionWatcherReDive.Inference.AI.Engines;

public abstract class InferenceEngineBase(
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
        5. Call get_tmdb_seasons to get TMDB's season/episode structure, then normalize:
           - If TMDB merges multiple cours into one season (e.g. S01 with 48 eps), map "S02E01" → S01E25.
           - If the title uses absolute numbering ("- 25"), find the correct TMDB season.
           - If TMDB has the referenced season, keep the episode number as-is.

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
        Logger.LogInformation("[{Provider}] Starting inference for title: {Title}", ProviderName, title);

        await RateLimitSemaphore.WaitAsync(cancellationToken);
        try
        {
            var elapsed = DateTime.UtcNow - _lastCallTime;
            var minInterval = TimeSpan.FromMilliseconds(Options.RateLimitDelayMs);
            if (elapsed < minInterval)
            {
                var delay = minInterval - elapsed;
                Logger.LogDebug("[{Provider}] Rate limiting: waiting {DelayMs}ms before next API call",
                    ProviderName, (int)delay.TotalMilliseconds);
                await Task.Delay(delay, cancellationToken);
            }

            var result = await InferCoreAsync(title, description, cancellationToken);
            _lastCallTime = DateTime.UtcNow;

            if (result != null)
                Logger.LogInformation(
                    "[{Provider}] Inference succeeded for title: {Title} -> TMDB: {TmdbId}, S{Season}E{Episode}",
                    ProviderName, title, result.TmdbId ?? "N/A", result.Season, result.Episode);
            else
                Logger.LogWarning("[{Provider}] Inference returned no result for title: {Title}", ProviderName, title);
            return result;
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "[{Provider}] Inference failed for title: {Title}", ProviderName, title);
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
            Logger.LogDebug("[{Provider}] Executing TMDB search with query: {Query}", ProviderName, query);
            var result = await tmdbTool.SearchAsync(query, cancellationToken);
            Logger.LogDebug("[{Provider}] TMDB search returned: {Result}", ProviderName, result);
            return result;
        }

        if (functionName == "get_tmdb_seasons")
        {
            var args = JsonNode.Parse(argumentsJson);
            var tmdbId = args?["tmdb_id"]?.GetValue<int>() ?? 0;
            Logger.LogDebug("[{Provider}] Getting TMDB season info for ID: {TmdbId}", ProviderName, tmdbId);
            var result = await tmdbTool.GetSeasonsAsync(tmdbId, cancellationToken);
            Logger.LogDebug("[{Provider}] TMDB seasons returned: {Result}", ProviderName, result);
            return result;
        }

        Logger.LogWarning("[{Provider}] Unknown tool call: {FunctionName}", ProviderName, functionName);
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
}
