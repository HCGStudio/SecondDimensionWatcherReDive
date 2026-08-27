using System.Text;
using System.Text.Json;
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
    IAIEngine aiEngine,
    IServiceProvider serviceProvider,
    IOptions<InferenceOptions> options,
    FileNameInferenceContext fileNameInferenceContext,
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

    private const string FileNameSystemPrompt = """
        You are a JSON-only anime filename inference API. You receive every video file in one downloaded release plus a list of target file paths that still need AI inference. Infer the season and episode for the target files. Use the full file list to understand and validate the release format.

        When regex tools are available, inspect the whole batch and call save_filename_regex_rule whenever a reusable filename pattern can directly extract the final episode numbers. The pattern must use .NET regex syntax, must contain a named capture group (?<episode>...), and may contain (?<season>...). Make it specific to the observed release format. The tool validates the rule against the whole batch, rejects conflicts with results already resolved by older rules, saves it, and returns the exact current files it matched and the extracted values. Use those returned matches in your final answer. Do not invent matches. Do not save a rule when the captured number needs arithmetic, an offset, or TMDB season normalization; infer those files directly instead.

        Use the TMDB tools when the release uses absolute episode numbering, merged cours, or an ambiguous season layout. Normalize the final season and episode using TMDB's actual season episode counts, just as you would for feed metadata.

        Output contract:
        • Exactly one raw JSON object and nothing else.
        • Schema: {"files":[{"file_path":"exact input file_path","season":int|null,"episode":int|null}]}
        • Include every target file exactly once. Preserve file_path byte-for-byte.
        • Use null episode only when the episode truly cannot be inferred.
        • No markdown fences, explanations, or extra keys outside the object.
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

    public async Task<IReadOnlyList<FileNameInferenceResult>> InferFileNamesAsync(
        FileNameInferenceRequest request,
        CancellationToken cancellationToken)
    {
        if (request.Files.Count == 0) return [];

        LogStartingFileNameInference(logger, request.Files.Count, request.Context);

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

            var result = await InferFileNamesCoreAsync(request, cancellationToken);
            _lastCallTime = DateTime.UtcNow;
            LogFileNameInferenceSucceeded(logger, result.Count, request.Files.Count);
            return result;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogFileNameInferenceFailed(logger, ex, request.Context);
            return [];
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

    private async Task<IReadOnlyList<FileNameInferenceResult>> InferFileNamesCoreAsync(
        FileNameInferenceRequest request,
        CancellationToken cancellationToken)
    {
        var filesJson = JsonSerializer.Serialize(request.Files, ToolJsonOptions.Options);
        var targets = request.TargetFilePaths ?? request.Files.Select(file => file.FilePath).ToList();
        var targetsJson = JsonSerializer.Serialize(targets, ToolJsonOptions.Options);
        var messages = new List<IMessage>
        {
            new SystemMessage(FileNameSystemPrompt),
            new UserMessage(
                $"Release context: {request.Context}\nAll files: {filesJson}\nTarget file paths: {targetsJson}")
        };

        var toolBuilder = new ToolExecutorBuilder(serviceProvider)
            .AddTool<SearchTmdbTool>()
            .AddTool<GetTmdbSeasonsTool>()
            .AddTool<GetTmdbSeasonEpisodesTool>();
        if (request.AllowRegexRuleCreation)
            toolBuilder.AddTool<SaveFileNameRegexRuleTool>();

        var toolExecutor = toolBuilder.Build();
        using var inferenceScope = request.AllowRegexRuleCreation
            ? fileNameInferenceContext.Push(request)
            : null;

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
                case ToolResultUpdate:
                    fullText.Clear();
                    break;
                case TextDelta td:
                    fullText.Append(td.Text);
                    break;
            }
        }

        return ParseFileNameInferenceResults(
            fullText.Length > 0 ? fullText.ToString() : null,
            request.Files);
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

    private static IReadOnlyList<FileNameInferenceResult> ParseFileNameInferenceResults(
        string? content,
        IReadOnlyList<FileNameInferenceInput> inputs)
    {
        if (string.IsNullOrWhiteSpace(content)) return [];

        var jsonStr = content.Trim();
        if (jsonStr.StartsWith("```", StringComparison.Ordinal))
        {
            var firstNewline = jsonStr.IndexOf('\n');
            if (firstNewline >= 0) jsonStr = jsonStr[(firstNewline + 1)..];
            if (jsonStr.EndsWith("```", StringComparison.Ordinal)) jsonStr = jsonStr[..^3];
            jsonStr = jsonStr.Trim();
        }

        var json = TryParseJson(jsonStr);
        if (json is null) return [];

        var files = json["files"]?.AsArray();
        if (files is null) return [];

        var validPaths = inputs.Select(input => input.FilePath).ToHashSet(StringComparer.Ordinal);
        var results = new Dictionary<string, FileNameInferenceResult>(StringComparer.Ordinal);
        foreach (var node in files)
        {
            try
            {
                var filePath = node?["file_path"]?.GetValue<string>();
                var episode = node?["episode"]?.GetValue<int?>();
                var season = node?["season"]?.GetValue<int?>();
                if (filePath is null || episode is null || episode < 0 || !validPaths.Contains(filePath))
                    continue;
                if (season < 0) continue;

                results[filePath] = new FileNameInferenceResult(filePath, season, episode.Value);
            }
            catch (InvalidOperationException)
            {
                // Ignore malformed entries while retaining valid results from the same response.
            }
        }

        return results.Values.ToList();
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

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Starting filename inference for {FileCount} files in release: {Context}")]
    private static partial void LogStartingFileNameInference(ILogger logger, int fileCount, string context);

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Filename inference resolved {ResolvedCount} of {FileCount} files")]
    private static partial void LogFileNameInferenceSucceeded(ILogger logger, int resolvedCount, int fileCount);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Filename inference failed for release: {Context}")]
    private static partial void LogFileNameInferenceFailed(ILogger logger, Exception ex, string context);
}
