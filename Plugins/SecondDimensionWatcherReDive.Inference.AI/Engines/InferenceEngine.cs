using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SecondDimensionWatcherReDive.AI.Abstractions;
using SecondDimensionWatcherReDive.AI.Models;
using SecondDimensionWatcherReDive.Framework.Inference;
using SecondDimensionWatcherReDive.Inference.AI.Configuration;
using SecondDimensionWatcherReDive.Inference.AI.Tools;

namespace SecondDimensionWatcherReDive.Inference.AI.Engines;

public sealed partial class InferenceEngine(
    IAiEngine aiEngine,
    IServiceProvider serviceProvider,
    IOptions<InferenceOptions> options,
    ILogger<InferenceEngine> logger) : IInferenceEngine
{
    private const string SystemPrompt = """
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

    private const int MaxToolRounds = 8;

    private static readonly SemaphoreSlim RateLimitSemaphore = new(1, 1);
    private static DateTime _lastCallTime = DateTime.MinValue;

    public async Task<InferenceResult?> InferAsync(string title, string description,
        CancellationToken cancellationToken)
    {
        LogStartingInference(logger, title);

        await RateLimitSemaphore.WaitAsync(cancellationToken);
        try
        {
            var elapsed = DateTime.UtcNow - _lastCallTime;
            var minInterval = TimeSpan.FromMilliseconds(options.Value.RateLimitDelayMs);
            if (elapsed < minInterval)
            {
                var delay = minInterval - elapsed;
                LogRateLimiting(logger, (int)delay.TotalMilliseconds);
                await Task.Delay(delay, cancellationToken);
            }

            var result = await InferCoreAsync(title, description, cancellationToken);
            _lastCallTime = DateTime.UtcNow;

            if (result != null)
                LogInferenceSucceeded(logger, title, result.TmdbId ?? "N/A", result.Season, result.Episode);
            else
                LogInferenceNoResult(logger, title);
            return result;
        }
        catch (Exception ex)
        {
            LogInferenceFailed(logger, ex, title);
            return null;
        }
        finally
        {
            RateLimitSemaphore.Release();
        }
    }

    private async Task<InferenceResult?> InferCoreAsync(
        string title, string description, CancellationToken cancellationToken)
    {
        var messages = new List<IMessage>
        {
            new SystemMessage(SystemPrompt),
            new UserMessage($"Title: {title}\nDescription: {description}")
        };

        var toolExecutor = new ToolExecutorBuilder(serviceProvider)
            .AddTool<SearchTmdbTool>()
            .AddTool<GetTmdbSeasonsTool>()
            .AddTool<GetTmdbSeasonEpisodesTool>()
            .Build();

        var chatOptions = new ChatOptions
        {
            ToolExecutor = toolExecutor,
            MaxToolRounds = MaxToolRounds
        };

        var fullText = new StringBuilder();
        await foreach (var update in aiEngine.ChatAsync(messages, chatOptions, cancellationToken))
        {
            switch (update)
            {
                // Discard pre-tool text so we only parse the final assistant message
                case ToolResultUpdate:
                    fullText.Clear();
                    break;
                case TextDelta td:
                    fullText.Append(td.Text);
                    break;
            }
        }

        return ParseInferenceResult(fullText.Length > 0 ? fullText.ToString() : null);
    }

    private static InferenceResult? ParseInferenceResult(string? content)
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

    [LoggerMessage(Level = LogLevel.Information, Message = "Starting inference for title: {Title}")]
    private static partial void LogStartingInference(ILogger logger, string title);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Rate limiting: waiting {DelayMs}ms before next API call")]
    private static partial void LogRateLimiting(ILogger logger, int delayMs);

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Inference succeeded for title: {Title} -> TMDB: {TmdbId}, S{Season}E{Episode}")]
    private static partial void LogInferenceSucceeded(ILogger logger, string title, string tmdbId, int? season,
        int? episode);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Inference returned no result for title: {Title}")]
    private static partial void LogInferenceNoResult(ILogger logger, string title);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Inference failed for title: {Title}")]
    private static partial void LogInferenceFailed(ILogger logger, Exception ex, string title);
}
