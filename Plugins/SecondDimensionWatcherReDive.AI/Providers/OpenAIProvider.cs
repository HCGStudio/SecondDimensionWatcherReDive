using System.Net.ServerSentEvents;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SecondDimensionWatcherReDive.AI.Abstractions;
using SecondDimensionWatcherReDive.AI.Configuration;
using SecondDimensionWatcherReDive.AI.Models;
using SecondDimensionWatcherReDive.AI.External;
using SecondDimensionWatcherReDive.Framework.AI;

namespace SecondDimensionWatcherReDive.AI.Providers;

public sealed partial class OpenAIProvider(
    IHttpClientFactory httpClientFactory,
    IOptions<OpenAIOptions> options,
    ILogger<OpenAIProvider> logger) : IAIProvider
{
    private const string HttpClientName = "OpenAI";

    public string ProviderName => "OpenAI";

    public async Task<IReadOnlyList<AIModel>> GetAvailableModelsAsync(CancellationToken cancellationToken)
    {
        var client = httpClientFactory.CreateClient(HttpClientName);
        using var response = await client.GetAsync("models", cancellationToken);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStreamAsync(cancellationToken);
        var result = await JsonSerializer.DeserializeAsync(json, OpenAIJsonContext.Default.OpenAIModelsResponse,
            cancellationToken);

        if (result?.Data is null) return [];

        return result.Data
            .Where(m => m.Id is not null)
            .Select(m => new AIModel(m.Id!, m.Id!, "OpenAI"))
            .ToList();
    }

    public async IAsyncEnumerable<IChatUpdate> StreamChatCompletionAsync(
        IReadOnlyList<IMessage> messages,
        IReadOnlyList<ToolDefinition>? tools,
        string? model,
        int? maxTokens,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var opts = options.Value;

        var request = new OpenAIChatRequest
        {
            Model = model ?? opts.Model,
            Messages = BuildMessages(messages),
            Tools = BuildTools(tools),
            Stream = true,
            MaxTokens = maxTokens ?? opts.MaxTokens
        };

        var toolCallBuilders = new Dictionary<int, (string Id, string Name, StringBuilder Args)>();
        string? finishReason = null;

        await foreach (var chunk in StreamRawAsync(request, cancellationToken))
        {
            if (chunk.Choices is null) continue;

            foreach (var choice in chunk.Choices)
            {
                finishReason ??= choice.FinishReason;

                if (choice.Delta?.Content is { } content)
                    yield return new TextDelta(content);

                if (choice.Delta?.ToolCalls is { } toolCallChunks)
                {
                    foreach (var tc in toolCallChunks)
                    {
                        if (!toolCallBuilders.TryGetValue(tc.Index, out var builder))
                        {
                            builder = (tc.Id ?? "", tc.Function?.Name ?? "", new());
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
        yield return new Finished(finishReason);
    }

    private async IAsyncEnumerable<OpenAIChatChunk> StreamRawAsync(
        OpenAIChatRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var client = httpClientFactory.CreateClient(HttpClientName);

        var jsonContent = JsonSerializer.SerializeToUtf8Bytes(request, OpenAIJsonContext.Default.OpenAIChatRequest);
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

            var chunk = JsonSerializer.Deserialize(data, OpenAIJsonContext.Default.OpenAIChatChunk);
            if (chunk is not null) yield return chunk;
        }
    }

    private static List<OpenAIMessage> BuildMessages(IReadOnlyList<IMessage> messages)
    {
        var result = new List<OpenAIMessage>(messages.Count);
        foreach (var msg in messages)
        {
            result.Add(msg switch
            {
                SystemMessage sys => new() { Role = "system", Content = sys.Content },
                UserMessage usr => new() { Role = "user", Content = usr.Content },
                AssistantMessage asst => new()
                {
                    Role = "assistant",
                    Content = asst.Content,
                    ToolCalls = asst.ToolCalls?.Select(tc => new OpenAIToolCall
                    {
                        Id = tc.Id,
                        Function = new() { Name = tc.Name, Arguments = tc.Arguments }
                    }).ToList()
                },
                ToolResultMessage tool => new()
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

    private static List<OpenAITool>? BuildTools(IReadOnlyList<ToolDefinition>? tools)
    {
        if (tools is null or { Count: 0 }) return null;

        return tools.Select(t => new OpenAITool
        {
            Function = new()
            {
                Name = t.Name,
                Description = t.Description,
                Parameters = t.ParametersSchema
            }
        }).ToList();
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "[OpenAI] Stream complete. finish_reason: {FinishReason}, tool_calls: {ToolCallCount}")]
    private static partial void LogStreamComplete(ILogger logger, string? finishReason, int toolCallCount);
}
