using System.ClientModel;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenAI;
using OpenAI.Chat;
using SecondDimensionWatcherReDive.Framework.Inference;
using SecondDimensionWatcherReDive.Inference.AI.Configuration;
using SecondDimensionWatcherReDive.Inference.AI.Tools;

namespace SecondDimensionWatcherReDive.Inference.AI.Engines;

public partial class OpenAiCompatibleEngine(
    TmdbTool tmdbTool,
    IOptions<InferenceOptions> options,
    ILogger<OpenAiCompatibleEngine> logger)
    : InferenceEngineBase(tmdbTool, options)
{
    protected override ILogger Logger => logger;

    protected override string ProviderName => "OpenAI";

    protected override async Task<InferenceResult?> InferCoreAsync(
        string title, string description, CancellationToken cancellationToken)
    {
        var opts = Options;

        var client = new ChatClient(
            model: opts.Model,
            credential: new ApiKeyCredential(opts.ApiKey),
            options: new OpenAIClientOptions { Endpoint = new Uri(opts.BaseUrl.TrimEnd('/')) });

        var messages = new List<ChatMessage>
        {
            new SystemChatMessage(SystemPrompt),
            new UserChatMessage($"Title: {title}\nDescription: {description}")
        };

        var chatOptions = new ChatCompletionOptions()
        {
            MaxOutputTokenCount = opts.MaxTokens
        };
        chatOptions.Tools.Add(ChatTool.CreateFunctionTool(
            "search_tmdb",
            "Search TMDB (The Movie Database) for an anime by name to get its TMDB ID and metadata.",
            BinaryData.FromString(SearchTmdbSchema)));
        chatOptions.Tools.Add(ChatTool.CreateFunctionTool(
            "get_tmdb_seasons",
            "Get the season/episode structure of a TV show from TMDB. Returns each season's episode_count. Use this after search_tmdb to check how seasons and episodes are organized, so you can normalize episode numbering.",
            BinaryData.FromString(GetTmdbSeasonsSchema)));
        chatOptions.Tools.Add(ChatTool.CreateFunctionTool(
            "get_tmdb_season_episodes",
            "Get individual episode details (episode number, name, air date, overview) for a specific season of a TV show. Use this when you need to verify episode mapping or resolve ambiguous numbering.",
            BinaryData.FromString(GetTmdbSeasonEpisodesSchema)));

        for (var round = 0; round < MaxToolRounds; round++)
        {
            LogOpenAiInferenceRound(logger, round + 1, MaxToolRounds, title);

            // Use streaming to accumulate the full response —
            // avoids multi-choice issues where non-standard wrappers split
            // text and tool_calls across separate choices.
            var textContent = new System.Text.StringBuilder();
            var toolCallBuilders = new Dictionary<int, (string Id, string Name, System.Text.StringBuilder Args)>();
            ChatFinishReason? finishReason = null;

            AsyncCollectionResult<StreamingChatCompletionUpdate> stream =
                client.CompleteChatStreamingAsync(messages, chatOptions, cancellationToken);

            await foreach (var update in stream)
            {
                finishReason ??= update.FinishReason;

                // Accumulate text content
                foreach (var part in update.ContentUpdate)
                    if (part.Text != null)
                        textContent.Append(part.Text);

                // Accumulate tool call deltas
                foreach (var tcUpdate in update.ToolCallUpdates)
                {
                    if (!toolCallBuilders.TryGetValue(tcUpdate.Index, out var builder))
                    {
                        builder = (tcUpdate.ToolCallId ?? "", tcUpdate.FunctionName ?? "", new System.Text.StringBuilder());
                        toolCallBuilders[tcUpdate.Index] = builder;
                    }

                    // ID and name may arrive in later chunks
                    if (tcUpdate.ToolCallId != null && string.IsNullOrEmpty(builder.Id))
                        toolCallBuilders[tcUpdate.Index] = (tcUpdate.ToolCallId, builder.Name, builder.Args);
                    if (tcUpdate.FunctionName != null && string.IsNullOrEmpty(builder.Name))
                        toolCallBuilders[tcUpdate.Index] = (builder.Id, tcUpdate.FunctionName, builder.Args);

                    if (tcUpdate.FunctionArgumentsUpdate != null)
                        toolCallBuilders[tcUpdate.Index].Args.Append(tcUpdate.FunctionArgumentsUpdate);
                }
            }

            LogOpenAiStreamComplete(logger, finishReason?.ToString(), toolCallBuilders.Count);

            if (toolCallBuilders.Count > 0)
            {
                // Build ChatToolCall list from accumulated deltas
                var toolCalls = toolCallBuilders
                    .OrderBy(kv => kv.Key)
                    .Select(kv => ChatToolCall.CreateFunctionToolCall(
                        kv.Value.Id, kv.Value.Name,
                        BinaryData.FromString(kv.Value.Args.ToString())))
                    .ToList();

                messages.Add(new AssistantChatMessage(toolCalls));

                foreach (var toolCall in toolCalls)
                {
                    LogOpenAiToolCall(logger, toolCall.FunctionName, toolCall.FunctionArguments.ToString());

                    var toolResult = await ExecuteToolCallAsync(
                        toolCall.FunctionName, toolCall.FunctionArguments.ToString(),
                        title, cancellationToken);

                    messages.Add(new ToolChatMessage(toolCall.Id, toolResult));
                }

                continue;
            }

            // Final response — parse accumulated text
            var finalText = textContent.Length > 0 ? textContent.ToString() : null;
            return ParseInferenceResult(finalText);
        }

        LogOpenAiExceededMaxRounds(logger, title);
        return null;
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "[OpenAI] Inference round {Round}/{MaxRounds} for title: {Title}")]
    private static partial void LogOpenAiInferenceRound(ILogger logger, int round, int maxRounds, string title);

    [LoggerMessage(Level = LogLevel.Debug, Message = "[OpenAI] Stream complete. finish_reason: {FinishReason}, tool_calls: {ToolCallCount}")]
    private static partial void LogOpenAiStreamComplete(ILogger logger, string? finishReason, int toolCallCount);

    [LoggerMessage(Level = LogLevel.Debug, Message = "[OpenAI] Tool call: {Function}, args: {Args}")]
    private static partial void LogOpenAiToolCall(ILogger logger, string function, string args);

    [LoggerMessage(Level = LogLevel.Warning, Message = "OpenAI inference exceeded max tool rounds for title: {Title}")]
    private static partial void LogOpenAiExceededMaxRounds(ILogger logger, string title);
}
