using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using SecondDimensionWatcherReDive.AI.Abstractions;
using SecondDimensionWatcherReDive.AI.Models;

namespace SecondDimensionWatcherReDive.AI.Engines;

public sealed partial class AIEngine(
    IAIProvider provider,
    ILogger<AIEngine> logger) : IAIEngine
{
    public Task<IReadOnlyList<AIModel>> GetAvailableModelsAsync(CancellationToken cancellationToken)
        => provider.GetAvailableModelsAsync(cancellationToken);

    public async IAsyncEnumerable<IChatUpdate> ChatAsync(
        IReadOnlyList<IMessage> messages,
        ChatOptions? options,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var maxToolRounds = options?.MaxToolRounds ?? 8;
        var tools = options?.ToolExecutor?.ToolDefinitions;
        var conversation = new List<IMessage>(messages);
        string? finishReason = null;

        for (var round = 0; round < maxToolRounds; round++)
        {
            LogInferenceRound(logger, provider.ProviderName, round + 1, maxToolRounds);

            var textContent = new StringBuilder();
            var toolCallBuilders = new Dictionary<string, (string Name, StringBuilder Args)>();
            finishReason = null;

            await foreach (var update in provider.StreamChatCompletionAsync(
                               conversation, tools, options?.Model, options?.MaxTokens, cancellationToken))
            {
                switch (update)
                {
                    case TextDelta td:
                        textContent.Append(td.Text);
                        yield return td;
                        break;
                    case ToolCallBegin tcb:
                        toolCallBuilders[tcb.Id] = (tcb.Name, new StringBuilder());
                        yield return tcb;
                        break;
                    case ToolCallDelta tcd:
                        if (toolCallBuilders.TryGetValue(tcd.Id, out var builder))
                            builder.Args.Append(tcd.ArgumentsDelta);
                        yield return tcd;
                        break;
                    case Finished f:
                        finishReason = f.StopReason;
                        // Do not yield yet — only after the final round
                        break;
                }
            }

            LogStreamComplete(logger, provider.ProviderName, finishReason, toolCallBuilders.Count);

            if (toolCallBuilders.Count == 0) break;

            // Build completed tool calls
            var completedCalls = toolCallBuilders
                .Select(kv => new ToolCall(kv.Key, kv.Value.Name, kv.Value.Args.ToString()))
                .ToList();

            // Add assistant message with tool calls to conversation
            conversation.Add(new AssistantMessage(
                textContent.Length > 0 ? textContent.ToString() : null,
                completedCalls));

            // Execute tools and add results to conversation
            if (options?.ToolExecutor is { } executor)
            {
                foreach (var toolCall in completedCalls)
                {
                    LogToolCall(logger, provider.ProviderName, toolCall.Name, toolCall.Arguments);
                    var toolResult = await executor.ExecuteAsync(toolCall, cancellationToken);
                    var json = JsonSerializer.SerializeToElement(
                        toolResult, toolResult.GetType(), ToolJsonOptions.Options);
                    yield return new ToolResultUpdate(toolCall.Id, json);

                    conversation.Add(new ToolResultMessage(toolCall.Id, json.GetRawText()));
                }
            }
        }

        yield return new Finished(finishReason);
    }

    [LoggerMessage(Level = LogLevel.Debug,
        Message = "[{Provider}] Chat round {Round}/{MaxRounds}")]
    private static partial void LogInferenceRound(
        ILogger logger, string provider, int round, int maxRounds);

    [LoggerMessage(Level = LogLevel.Debug,
        Message = "[{Provider}] Stream complete. stop_reason: {StopReason}, tool_calls: {ToolCallCount}")]
    private static partial void LogStreamComplete(
        ILogger logger, string provider, string? stopReason, int toolCallCount);

    [LoggerMessage(Level = LogLevel.Debug,
        Message = "[{Provider}] Tool call: {ToolName}, args: {Args}")]
    private static partial void LogToolCall(
        ILogger logger, string provider, string toolName, string args);
}
