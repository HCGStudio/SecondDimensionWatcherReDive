using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SecondDimensionWatcherReDive.Framework.Inference;
using SecondDimensionWatcherReDive.Inference.AI.Configuration;
using SecondDimensionWatcherReDive.Inference.AI.Tools;

namespace SecondDimensionWatcherReDive.Inference.AI.Engines;

public abstract class InferenceEngineBase(
    IHttpClientFactory httpClientFactory,
    TmdbTool tmdbTool,
    IOptions<InferenceOptions> options) : IInferenceEngine
{
    protected const string SystemPrompt = """
        You are an anime metadata extraction assistant. Given a feed item title and description from an anime torrent RSS feed, extract structured metadata.

        Common title formats:
        - [GroupName] Anime Name - 05 [1080p][HEVC]
        - [GroupName] Anime Name S02E05 [WebRip 1080p]
        - 【GroupName】Anime Name 第05話
        - [GroupName] Anime Name / Alternative Name - 05 (1920x1080)

        Instructions:
        1. Extract the subtitle/fansub group name (usually in brackets at the start)
        2. Extract the anime name
        3. Extract the season number (default to 1 if not explicitly specified)
        4. Extract the episode number. If the torrent contains multiple episodes (e.g. batch release "01-12", "Vol.1", "Complete"), set episode to null.
        5. Use the search_tmdb tool to look up the anime and get its TMDB ID
        6. Write a brief description of the anime (1-2 sentences in the same language as the title, summarizing the show's premise)

        Return your final answer as a JSON object with these keys:
        {
            "animation_name": "string - the anime name",
            "original_name": "string - original Japanese/Chinese name if available, otherwise same as animation_name",
            "description": "string or null - a brief 1-2 sentence description of the anime",
            "tmdb_id": "string or null - TMDB ID from search results",
            "group_name": "string or null - the subtitle group name",
            "season": "integer or null - season number",
            "episode": "integer or null - single episode number, null if the torrent contains multiple episodes"
        }

        Return ONLY the JSON object, no other text.
        """;

    protected const int MaxToolRounds = 5;

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
                    "[{Provider}] Inference succeeded for title: {Title} -> {AnimationName} (TMDB: {TmdbId}, S{Season}E{Episode})",
                    ProviderName, title, result.AnimationName, result.TmdbId ?? "N/A",
                    result.Season, result.Episode);
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

    protected async Task<HttpResponseMessage> SendRequestAsync(
        string endpoint, Action<HttpRequestMessage> configureRequest, JsonObject requestBody,
        CancellationToken cancellationToken)
    {
        var httpClient = httpClientFactory.CreateClient("InferenceEngine");
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
        configureRequest(request);
        request.Content = new StringContent(requestBody.ToJsonString(), Encoding.UTF8, "application/json");
        Logger.LogDebug("[{Provider}] Sending request to {Endpoint}", ProviderName, endpoint);
        return await httpClient.SendAsync(request, cancellationToken);
    }

    protected async Task<string> ExecuteToolCallAsync(
        string? query, string fallbackTitle, CancellationToken cancellationToken)
    {
        var actualQuery = query ?? fallbackTitle;
        Logger.LogDebug("[{Provider}] Executing TMDB search with query: {Query}", ProviderName, actualQuery);
        var result = await tmdbTool.SearchAsync(actualQuery, cancellationToken);
        Logger.LogDebug("[{Provider}] TMDB search returned: {Result}", ProviderName, result);
        return result;
    }

    protected static InferenceResult? ParseInferenceResult(string? content)
    {
        if (string.IsNullOrWhiteSpace(content)) return null;

        var jsonStr = content.Trim();
        if (jsonStr.StartsWith("```"))
        {
            var firstNewline = jsonStr.IndexOf('\n');
            if (firstNewline >= 0) jsonStr = jsonStr[(firstNewline + 1)..];
            if (jsonStr.EndsWith("```")) jsonStr = jsonStr[..^3];
            jsonStr = jsonStr.Trim();
        }

        JsonNode? json;
        try
        {
            json = JsonNode.Parse(jsonStr);
        }
        catch (System.Text.Json.JsonException)
        {
            return null;
        }

        if (json == null) return null;

        var animationName = json["animation_name"]?.GetValue<string>();
        if (string.IsNullOrEmpty(animationName)) return null;

        return new InferenceResult(
            AnimationName: animationName,
            OriginalName: json["original_name"]?.GetValue<string>() ?? animationName,
            Description: json["description"]?.GetValue<string>(),
            TmdbId: json["tmdb_id"]?.GetValue<string>(),
            GroupName: json["group_name"]?.GetValue<string>(),
            Season: json["season"]?.GetValue<int?>(),
            Episode: json["episode"]?.GetValue<int?>());
    }
}
