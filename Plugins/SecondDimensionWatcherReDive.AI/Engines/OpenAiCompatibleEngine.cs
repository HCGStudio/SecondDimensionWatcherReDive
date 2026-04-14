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

public sealed partial class OpenAiCompatibleEngine(
    IHttpClientFactory httpClientFactory,
    IOptions<OpenAiOptions> engineOptions,
    ILogger<OpenAiCompatibleEngine> logger) : IAiEngine
{
    private const string HttpClientName = "OpenAi";

    public async Task<IReadOnlyList<AiModel>> GetAvailableModelsAsync(CancellationToken cancellationToken)
    {
        var client = httpClientFactory.CreateClient(HttpClientName);
        using var response = await client.GetAsync("models", cancellationToken);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStreamAsync(cancellationToken);
        var result = await JsonSerializer.DeserializeAsync(json, OpenAiJsonContext.Default.OpenAiModelsResponse,
            cancellationToken);

        if (result?.Data is null) return [];

        return result.Data
            .Where(m => m.Id is not null)
            .Select(m => new AiModel(m.Id!, m.Id!, "OpenAI"))
            .ToList();
    }

    public async IAsyncEnumerable<IChatUpdate> ChatAsync(
        IReadOnlyList<IMessage> messages,
        ChatOptions? options,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var opts = engineOptions.Value;
        var maxToolRounds = options?.MaxToolRounds ?? 8;
        var maxTokens = options?.MaxTokens ?? opts.MaxTokens;

        var conversationMessages = BuildMessages(messages);
        var tools = BuildTools(options?.Tools);
        string? finishReason = null;

        for (var round = 0; round < maxToolRounds; round++)
        {
            LogInferenceRound(logger, round + 1, maxToolRounds);

            var request = new OpenAiChatRequest
            {
                Model = opts.Model,
                Messages = conversationMessages,
                Tools = tools is { Count: > 0 } ? tools : null,
                Stream = true,
                MaxTokens = maxTokens
            };

            var textContent = new StringBuilder();
            var toolCallBuilders = new Dictionary<int, (string Id, string Name, StringBuilder Args)>();
            finishReason = null;

            await foreach (var chunk in StreamCompletionAsync(request, cancellationToken))
            {
                if (chunk.Choices is null) continue;

                foreach (var choice in chunk.Choices)
                {
                    finishReason ??= choice.FinishReason;

                    if (choice.Delta?.Content is { } content)
                    {
                        textContent.Append(content);
                        yield return new TextDelta(content);
                    }

                    if (choice.Delta?.ToolCalls is { } toolCallChunks)
                    {
                        foreach (var tc in toolCallChunks)
                        {
                            if (!toolCallBuilders.TryGetValue(tc.Index, out var builder))
                            {
                                builder = (tc.Id ?? "", tc.Function?.Name ?? "", new StringBuilder());
                                toolCallBuilders[tc.Index] = builder;

                                if (!string.IsNullOrEmpty(tc.Id) && !string.IsNullOrEmpty(tc.Function?.Name))
                                    yield return new ToolCallBegin(tc.Id, tc.Function.Name);
                            }
                            else
                            {
                                if (tc.Id is not null && string.IsNullOrEmpty(builder.Id))
                                    toolCallBuilders[tc.Index] = (tc.Id, builder.Name, builder.Args);
                                if (tc.Function?.Name is not null && string.IsNullOrEmpty(builder.Name))
                                {
                                    toolCallBuilders[tc.Index] = (builder.Id, tc.Function.Name, builder.Args);
                                    yield return new ToolCallBegin(
                                        toolCallBuilders[tc.Index].Id, tc.Function.Name);
                                }
                            }

                            if (tc.Function?.Arguments is { } argsDelta)
                            {
                                toolCallBuilders[tc.Index].Args.Append(argsDelta);
                                yield return new ToolCallDelta(
                                    toolCallBuilders[tc.Index].Id, argsDelta);
                            }
                        }
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

            // Add assistant message with tool calls to conversation
            var assistantToolCalls = completedCalls
                .Select(tc => new OpenAiToolCallDto
                {
                    Id = tc.Id,
                    Function = new OpenAiFunctionCall { Name = tc.Name, Arguments = tc.Arguments }
                })
                .ToList();

            conversationMessages.Add(new OpenAiMessage
            {
                Role = "assistant",
                Content = textContent.Length > 0 ? textContent.ToString() : null,
                ToolCalls = assistantToolCalls
            });

            // Execute tools
            if (options?.ToolExecutor is { } executor)
            {
                foreach (var toolCall in completedCalls)
                {
                    LogToolCall(logger, toolCall.Name, toolCall.Arguments);
                    var result = await executor(toolCall, cancellationToken);
                    yield return new ToolResultUpdate(toolCall.Id, result);

                    conversationMessages.Add(new OpenAiMessage
                    {
                        Role = "tool",
                        Content = result,
                        ToolCallId = toolCall.Id
                    });
                }
            }
        }

        yield return new Finished(finishReason);
    }

    private async IAsyncEnumerable<OpenAiChatChunk> StreamCompletionAsync(
        OpenAiChatRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var client = httpClientFactory.CreateClient(HttpClientName);

        var jsonContent = JsonSerializer.SerializeToUtf8Bytes(request, OpenAiJsonContext.Default.OpenAiChatRequest);
        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, "chat/completions")
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
            var data = item.Data;
            if (data is "[DONE]") yield break;

            var chunk = JsonSerializer.Deserialize(data, OpenAiJsonContext.Default.OpenAiChatChunk);
            if (chunk is not null) yield return chunk;
        }
    }

    private static List<OpenAiMessage> BuildMessages(IReadOnlyList<IMessage> messages)
    {
        var result = new List<OpenAiMessage>(messages.Count);
        foreach (var msg in messages)
        {
            result.Add(msg switch
            {
                SystemMessage sys => new OpenAiMessage { Role = "system", Content = sys.Content },
                UserMessage usr => new OpenAiMessage { Role = "user", Content = usr.Content },
                AssistantMessage asst => new OpenAiMessage
                {
                    Role = "assistant",
                    Content = asst.Content,
                    ToolCalls = asst.ToolCalls?.Select(tc => new OpenAiToolCallDto
                    {
                        Id = tc.Id,
                        Function = new OpenAiFunctionCall { Name = tc.Name, Arguments = tc.Arguments }
                    }).ToList()
                },
                ToolResultMessage tool => new OpenAiMessage
                {
                    Role = "tool",
                    Content = tool.Content,
                    ToolCallId = tool.ToolCallId
                },
                _ => throw new ArgumentException($"Unknown message type: {msg.GetType().Name}")
            });
        }

        return result;
    }

    private static List<OpenAiTool>? BuildTools(IReadOnlyList<ToolDefinition>? tools)
    {
        if (tools is null or { Count: 0 }) return null;

        return tools.Select(t => new OpenAiTool
        {
            Function = new OpenAiFunctionDef
            {
                Name = t.Name,
                Description = t.Description,
                Parameters = t.ParametersSchema
            }
        }).ToList();
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "[OpenAI] Chat round {Round}/{MaxRounds}")]
    private static partial void LogInferenceRound(ILogger logger, int round, int maxRounds);

    [LoggerMessage(Level = LogLevel.Debug, Message = "[OpenAI] Stream complete. finish_reason: {FinishReason}, tool_calls: {ToolCallCount}")]
    private static partial void LogStreamComplete(ILogger logger, string? finishReason, int toolCallCount);

    [LoggerMessage(Level = LogLevel.Debug, Message = "[OpenAI] Tool call: {Function}, args: {Args}")]
    private static partial void LogToolCall(ILogger logger, string function, string args);
}
