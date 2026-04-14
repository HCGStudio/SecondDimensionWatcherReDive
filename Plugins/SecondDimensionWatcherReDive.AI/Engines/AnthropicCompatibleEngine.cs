using System.Net.ServerSentEvents;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SecondDimensionWatcherReDive.AI.Abstractions;
using SecondDimensionWatcherReDive.AI.Configuration;
using SecondDimensionWatcherReDive.AI.Models;
using SecondDimensionWatcherReDive.AI.Serialization;

namespace SecondDimensionWatcherReDive.AI.Engines;

public sealed partial class AnthropicCompatibleEngine(
    IHttpClientFactory httpClientFactory,
    IOptions<AnthropicOptions> options,
    ILogger<AnthropicCompatibleEngine> logger) : IAiEngine
{
    private const string HttpClientName = "AnthropicAi";

    public async Task<IReadOnlyList<AiModel>> GetAvailableModelsAsync(CancellationToken cancellationToken)
    {
        var client = httpClientFactory.CreateClient(HttpClientName);
        using var response = await client.GetAsync("v1/models", cancellationToken);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStreamAsync(cancellationToken);
        var result = await JsonSerializer.DeserializeAsync(json,
            AnthropicJsonContext.Default.AnthropicModelsResponse, cancellationToken);

        if (result?.Data is null) return [];

        return result.Data
            .Where(m => m.Id is not null)
            .Select(m => new AiModel(m.Id!, m.DisplayName ?? m.Id!, "Anthropic"))
            .ToList();
    }

    public async IAsyncEnumerable<IChatUpdate> ChatAsync(
        IReadOnlyList<IMessage> messages,
        ChatOptions? chatOptions,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var opts = options.Value;
        var maxToolRounds = chatOptions?.MaxToolRounds ?? 8;
        var maxTokens = chatOptions?.MaxTokens ?? opts.MaxTokens;

        // Extract system message and build conversation messages
        string? systemPrompt = null;
        var conversationMessages = new List<AnthropicMessage>();
        foreach (var msg in messages)
        {
            switch (msg)
            {
                case SystemMessage sys:
                    systemPrompt = sys.Content;
                    break;
                case UserMessage usr:
                    conversationMessages.Add(new AnthropicMessage
                    {
                        Role = "user",
                        Content = [new AnthropicContentBlock { Type = "text", Text = usr.Content }]
                    });
                    break;
                case AssistantMessage asst:
                    conversationMessages.Add(BuildAssistantMessage(asst));
                    break;
                case ToolResultMessage tool:
                    AppendToolResult(conversationMessages, tool);
                    break;
            }
        }

        var tools = BuildTools(chatOptions?.Tools);
        string? finishReason = null;

        for (var round = 0; round < maxToolRounds; round++)
        {
            LogInferenceRound(logger, round + 1, maxToolRounds);

            var request = new AnthropicMessagesRequest
            {
                Model = opts.Model,
                MaxTokens = maxTokens,
                System = systemPrompt,
                Messages = conversationMessages,
                Tools = tools is { Count: > 0 } ? tools : null,
                Stream = true
            };

            var textContent = new StringBuilder();
            var toolCallBuilders = new Dictionary<int, (string Id, string Name, StringBuilder Args)>();
            finishReason = null;

            await foreach (var (eventType, data) in StreamMessagesAsync(request, cancellationToken))
            {
                switch (eventType)
                {
                    case "content_block_start":
                    {
                        var parsed = JsonSerializer.Deserialize(data,
                            AnthropicJsonContext.Default.AnthropicContentBlockStartData);
                        if (parsed?.ContentBlock is { } block)
                        {
                            if (block.Type == "tool_use" && block.Id is not null && block.Name is not null)
                            {
                                toolCallBuilders[parsed.Index] = (block.Id, block.Name, new StringBuilder());
                                yield return new ToolCallBegin(block.Id, block.Name);
                            }
                        }

                        break;
                    }
                    case "content_block_delta":
                    {
                        var parsed = JsonSerializer.Deserialize(data,
                            AnthropicJsonContext.Default.AnthropicContentBlockDeltaData);
                        if (parsed?.Delta is { } delta)
                        {
                            if (delta.Type == "text_delta" && delta.Text is not null)
                            {
                                textContent.Append(delta.Text);
                                yield return new TextDelta(delta.Text);
                            }
                            else if (delta.Type == "input_json_delta" && delta.PartialJson is not null)
                            {
                                if (toolCallBuilders.TryGetValue(parsed.Index, out var builder))
                                {
                                    builder.Args.Append(delta.PartialJson);
                                    yield return new ToolCallDelta(builder.Id, delta.PartialJson);
                                }
                            }
                        }

                        break;
                    }
                    case "message_delta":
                    {
                        var parsed = JsonSerializer.Deserialize(data,
                            AnthropicJsonContext.Default.AnthropicMessageDeltaData);
                        finishReason = parsed?.Delta?.StopReason;
                        break;
                    }
                }
            }

            LogStreamComplete(logger, finishReason, toolCallBuilders.Count);

            if (toolCallBuilders.Count == 0) break;

            // Build completed tool calls
            var completedCalls = toolCallBuilders
                .OrderBy(kv => kv.Key)
                .Select(kv => new Models.ToolCall(kv.Value.Id, kv.Value.Name, kv.Value.Args.ToString()))
                .ToList();

            // Add assistant message to conversation history
            var assistantBlocks = new List<AnthropicContentBlock>();
            if (textContent.Length > 0)
                assistantBlocks.Add(new AnthropicContentBlock { Type = "text", Text = textContent.ToString() });
            foreach (var tc in completedCalls)
            {
                assistantBlocks.Add(new AnthropicContentBlock
                {
                    Type = "tool_use",
                    Id = tc.Id,
                    Name = tc.Name,
                    Input = JsonSerializer.Deserialize<JsonElement>(
                        string.IsNullOrEmpty(tc.Arguments) ? "{}" : tc.Arguments)
                });
            }

            conversationMessages.Add(new AnthropicMessage { Role = "assistant", Content = assistantBlocks });

            // Execute tools and add results as a single user message
            if (chatOptions?.ToolExecutor is { } executor)
            {
                var resultBlocks = new List<AnthropicContentBlock>();
                foreach (var toolCall in completedCalls)
                {
                    LogToolCall(logger, toolCall.Name, toolCall.Arguments);
                    var result = await executor(toolCall, cancellationToken);
                    yield return new ToolResultUpdate(toolCall.Id, result);

                    resultBlocks.Add(new AnthropicContentBlock
                    {
                        Type = "tool_result",
                        ToolUseId = toolCall.Id,
                        ResultContent = result
                    });
                }

                conversationMessages.Add(new AnthropicMessage { Role = "user", Content = resultBlocks });
            }
        }

        yield return new Finished(finishReason);
    }

    private async IAsyncEnumerable<(string EventType, string Data)> StreamMessagesAsync(
        AnthropicMessagesRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var client = httpClientFactory.CreateClient(HttpClientName);

        var jsonContent = JsonSerializer.SerializeToUtf8Bytes(request,
            AnthropicJsonContext.Default.AnthropicMessagesRequest);
        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, "v1/messages")
        {
            Content = new ByteArrayContent(jsonContent)
        };
        httpRequest.Content.Headers.ContentType = new("application/json");

        using var response = await client.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        await foreach (var item in SseParser.Create(stream).EnumerateAsync(cancellationToken))
        {
            var eventType = item.EventType;

            // Skip ping and message_stop events
            if (eventType is "ping" or "message_stop")
                continue;

            yield return (eventType, item.Data);
        }
    }

    private static AnthropicMessage BuildAssistantMessage(AssistantMessage asst)
    {
        var blocks = new List<AnthropicContentBlock>();
        if (!string.IsNullOrEmpty(asst.Content))
            blocks.Add(new AnthropicContentBlock { Type = "text", Text = asst.Content });

        if (asst.ToolCalls is not null)
        {
            foreach (var tc in asst.ToolCalls)
            {
                blocks.Add(new AnthropicContentBlock
                {
                    Type = "tool_use",
                    Id = tc.Id,
                    Name = tc.Name,
                    Input = JsonSerializer.Deserialize<JsonElement>(
                        string.IsNullOrEmpty(tc.Arguments) ? "{}" : tc.Arguments)
                });
            }
        }

        return new AnthropicMessage { Role = "assistant", Content = blocks };
    }

    /// <summary>
    ///     Appends a tool result to the last user message, or creates a new one.
    ///     Anthropic requires tool results to be in user-role messages.
    /// </summary>
    private static void AppendToolResult(List<AnthropicMessage> messages, ToolResultMessage tool)
    {
        var block = new AnthropicContentBlock
        {
            Type = "tool_result",
            ToolUseId = tool.ToolCallId,
            ResultContent = tool.Content
        };

        // Try to append to the last user message if it contains tool results
        if (messages.Count > 0 && messages[^1] is { Role: "user" } lastMsg &&
            lastMsg.Content.Count > 0 && lastMsg.Content[0].Type == "tool_result")
        {
            lastMsg.Content.Add(block);
        }
        else
        {
            messages.Add(new AnthropicMessage
            {
                Role = "user",
                Content = [block]
            });
        }
    }

    private static List<AnthropicTool>? BuildTools(IReadOnlyList<ToolDefinition>? tools)
    {
        if (tools is null or { Count: 0 }) return null;

        return tools.Select(t => new AnthropicTool
        {
            Name = t.Name,
            Description = t.Description,
            InputSchema = t.ParametersSchema
        }).ToList();
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "[Anthropic] Chat round {Round}/{MaxRounds}")]
    private static partial void LogInferenceRound(ILogger logger, int round, int maxRounds);

    [LoggerMessage(Level = LogLevel.Debug, Message = "[Anthropic] Stream complete. stop_reason: {StopReason}, tool_calls: {ToolCallCount}")]
    private static partial void LogStreamComplete(ILogger logger, string? stopReason, int toolCallCount);

    [LoggerMessage(Level = LogLevel.Debug, Message = "[Anthropic] Tool call: {ToolName}, input: {Input}")]
    private static partial void LogToolCall(ILogger logger, string toolName, string input);
}
