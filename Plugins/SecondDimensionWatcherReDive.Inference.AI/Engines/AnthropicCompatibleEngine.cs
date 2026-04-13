using Anthropic.SDK;
using Anthropic.SDK.Common;
using Anthropic.SDK.Messaging;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SecondDimensionWatcherReDive.Framework.Inference;
using SecondDimensionWatcherReDive.Inference.AI.Configuration;
using SecondDimensionWatcherReDive.Inference.AI.Tools;
using ContentBase = Anthropic.SDK.Messaging.ContentBase;
using Message = Anthropic.SDK.Messaging.Message;
using Tool = Anthropic.SDK.Common.Tool;

namespace SecondDimensionWatcherReDive.Inference.AI.Engines;

public partial class AnthropicCompatibleEngine(
    TmdbTool tmdbTool,
    IOptions<InferenceOptions> options,
    ILogger<AnthropicCompatibleEngine> logger)
    : InferenceEngineBase(tmdbTool, options)
{
    protected override ILogger Logger => logger;

    protected override string ProviderName => "Anthropic";

    private static List<Tool> BuildTools()
    {
        // Use Tool.FromFunc with placeholder delegates — we'll dispatch manually
        return
        [
            Tool.FromFunc(
                "search_tmdb",
                ([FunctionParameter("query", true)] string query) => query,
                "Search TMDB (The Movie Database) for an anime by name to get its TMDB ID and metadata."),
            Tool.FromFunc(
                "get_tmdb_seasons",
                ([FunctionParameter("tmdb_id", true)] int tmdbId) => tmdbId.ToString(),
                "Get the season/episode structure of a TV show from TMDB. Returns each season's episode_count. Use this after search_tmdb to check how seasons and episodes are organized, so you can normalize episode numbering."),
            Tool.FromFunc(
                "get_tmdb_season_episodes",
                ([FunctionParameter("tmdb_id", true)] int tmdbId,
                 [FunctionParameter("season_number", true)] int seasonNumber) => $"{tmdbId}/{seasonNumber}",
                "Get individual episode details (episode number, name, air date, overview) for a specific season of a TV show. Use this when you need to verify episode mapping or resolve ambiguous numbering.")
        ];
    }

    protected override async Task<InferenceResult?> InferCoreAsync(
        string title, string description, CancellationToken cancellationToken)
    {
        var opts = Options;

        var httpClient = new HttpClient { BaseAddress = new Uri(opts.BaseUrl.TrimEnd('/')) };
        using var client = new AnthropicClient(new APIAuthentication(opts.ApiKey), httpClient);

        var messages = new List<Message>
        {
            new() { Role = RoleType.User, Content = [new TextContent { Text = $"Title: {title}\nDescription: {description}" }] }
        };

        var tools = BuildTools();

        for (var round = 0; round < MaxToolRounds; round++)
        {
            LogAnthropicInferenceRound(logger, round + 1, MaxToolRounds, title);

            var parameters = new MessageParameters
            {
                Model = opts.Model,
                MaxTokens = opts.MaxTokens,
                System = [new SystemMessage(SystemPrompt)],
                Messages = messages,
                Tools = tools
            };

            var response = await client.Messages.GetClaudeMessageAsync(parameters, cancellationToken);
            LogAnthropicStopReason(logger, response.StopReason);

            // Add assistant message to history
            messages.Add(response.Message);

            // Check for tool use in the response content
            var toolUseBlocks = response.Content?.OfType<ToolUseContent>().ToList() ?? [];
            if (toolUseBlocks.Count > 0)
            {
                var toolResults = new List<ContentBase>();

                foreach (var toolUse in toolUseBlocks)
                {
                    LogAnthropicToolCall(logger, toolUse.Name, toolUse.Input?.ToString());

                    var argumentsJson = toolUse.Input?.ToString() ?? "{}";
                    var result = await ExecuteToolCallAsync(
                        toolUse.Name, argumentsJson, title, cancellationToken);

                    toolResults.Add(new ToolResultContent
                    {
                        ToolUseId = toolUse.Id,
                        Content = [new TextContent { Text = result }]
                    });
                }

                messages.Add(new Message
                {
                    Role = RoleType.User,
                    Content = toolResults
                });

                continue;
            }

            // Final response — extract text content
            var textContent = response.Content?.OfType<TextContent>().FirstOrDefault()?.Text;
            return ParseInferenceResult(textContent);
        }

        LogAnthropicExceededMaxRounds(logger, title);
        return null;
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "[Anthropic] Inference round {Round}/{MaxRounds} for title: {Title}")]
    private static partial void LogAnthropicInferenceRound(ILogger logger, int round, int maxRounds, string title);

    [LoggerMessage(Level = LogLevel.Debug, Message = "[Anthropic] Response stop_reason: {StopReason}")]
    private static partial void LogAnthropicStopReason(ILogger logger, string? stopReason);

    [LoggerMessage(Level = LogLevel.Debug, Message = "[Anthropic] Tool call: {ToolName}, input: {Input}")]
    private static partial void LogAnthropicToolCall(ILogger logger, string toolName, string? input);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Anthropic inference exceeded max tool rounds for title: {Title}")]
    private static partial void LogAnthropicExceededMaxRounds(ILogger logger, string title);
}
