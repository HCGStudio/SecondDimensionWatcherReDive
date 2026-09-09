using System.Net.ServerSentEvents;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SecondDimensionWatcherReDive.AI.Abstractions;
using SecondDimensionWatcherReDive.AI.Configuration;
using SecondDimensionWatcherReDive.AI.Models;
using SecondDimensionWatcherReDive.AI.External;
using SecondDimensionWatcherReDive.Framework.AI;

namespace SecondDimensionWatcherReDive.AI.Providers;

public sealed partial class AnthropicProvider : IAIProvider
{
    private const string HttpClientName = "AnthropicAI";
    private readonly Func<AnthropicOptions> _getOptions;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<AnthropicProvider> _logger;

    [ActivatorUtilitiesConstructor]
    public AnthropicProvider(
        IHttpClientFactory httpClientFactory,
        IOptionsMonitor<AnthropicOptions> options,
        ILogger<AnthropicProvider> logger)
    {
        _httpClientFactory = httpClientFactory;
        _getOptions = () => options.CurrentValue;
        _logger = logger;
    }

    /// <summary>
    ///     Preserves the original snapshot-options constructor for direct callers. Runtime DI uses
    ///     the monitor overload so saved settings apply to subsequent requests.
    /// </summary>
    public AnthropicProvider(
        IHttpClientFactory httpClientFactory,
        IOptions<AnthropicOptions> options,
        ILogger<AnthropicProvider> logger)
    {
        _httpClientFactory = httpClientFactory;
        _getOptions = () => options.Value;
        _logger = logger;
    }

    public string ProviderName => "Anthropic";

    public bool IsConfigured => IsValidConfiguration(_getOptions());

    public async Task<IReadOnlyList<AIModel>> GetAvailableModelsAsync(CancellationToken cancellationToken)
    {
        var opts = Snapshot(GetConfiguredOptions());
        var client = _httpClientFactory.CreateClient(HttpClientName);
        var models = new List<AIModel>();
        var seenCursors = new HashSet<string>(StringComparer.Ordinal);
        string? cursor = null;
        do
        {
            var path = "v1/models?limit=100";
            if (cursor is not null) path += "&after_id=" + Uri.EscapeDataString(cursor);
            using var request = CreateRequest(HttpMethod.Get, opts, path);
            using var response = await client.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();
            await using var json = await response.Content.ReadAsStreamAsync(cancellationToken);
            var result = await JsonSerializer.DeserializeAsync(json,
                AnthropicJsonContext.Default.AnthropicModelsResponse, cancellationToken);
            if (result?.Data is { } data)
                models.AddRange(data.Where(m => !string.IsNullOrWhiteSpace(m.Id))
                    .Select(m => new AIModel(m.Id!, m.DisplayName ?? m.Id!, "Anthropic")
                    {
                        ProviderId = "anthropic",
                        ReasoningEfforts = AIModelCapabilities.GetReasoningEfforts(AIProviderProtocol.Anthropic, m.Id!)
                    }));
            cursor = result?.HasMore == true ? result.LastId ?? result.Data?.LastOrDefault()?.Id : null;
            if (result?.HasMore == true && (cursor is null || !seenCursors.Add(cursor)))
                throw new InvalidDataException("Anthropic repeated or omitted a model-list cursor.");
        } while (cursor is not null);
        models.Add(new AIModel(opts.Model, opts.Model, "Anthropic")
        {
            ProviderId = "anthropic",
            ReasoningEfforts = AIModelCapabilities.GetReasoningEfforts(AIProviderProtocol.Anthropic, opts.Model)
        });
        return models.DistinctBy(model => model.Id).OrderByDescending(model => model.Id == opts.Model).ToList();
    }

    public IAsyncEnumerable<IChatUpdate> StreamChatCompletionAsync(
        IReadOnlyList<IMessage> messages,
        IReadOnlyList<ToolDefinition>? tools,
        string? model,
        int? maxTokens,
        IAIProviderContinuation? continuation,
        CancellationToken cancellationToken)
        => StreamChatCompletionAsync(messages, tools, model, maxTokens, continuation, null, cancellationToken);

    public async IAsyncEnumerable<IChatUpdate> StreamChatCompletionAsync(
        IReadOnlyList<IMessage> messages,
        IReadOnlyList<ToolDefinition>? tools,
        string? model,
        int? maxTokens,
        IAIProviderContinuation? continuation,
        string? reasoningEffort,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        // Pin the endpoint and credential for every round of one conversation.
        var opts = continuation switch
        {
            null => Snapshot(GetConfiguredOptions()),
            AnthropicContinuation state => state.Options,
            _ => throw new InvalidOperationException(
                $"Unsupported Anthropic continuation state: {continuation.GetType().Name}")
        };

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
                    conversationMessages.Add(new()
                    {
                        Role = "user",
                        Content = [new() { Type = "text", Text = usr.Content }]
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

        if (continuation is AnthropicContinuation previous)
        {
            conversationMessages = new List<AnthropicMessage>(previous.Messages);
            var trailingResults = messages.Reverse().TakeWhile(message => message is ToolResultMessage).Reverse();
            foreach (var result in trailingResults.Cast<ToolResultMessage>())
                AppendToolResult(conversationMessages, result);
        }
        var selectedModel = model ?? opts.Model;
        var supportsAdaptiveThinking = AIModelCapabilities.GetReasoningEfforts(
            AIProviderProtocol.Anthropic, selectedModel).Count > 0 &&
            !AIModelCapabilities.IsModel(selectedModel, "claude-opus-4-5");
        var request = new AnthropicMessagesRequest
        {
            Model = selectedModel,
            MaxTokens = maxTokens ?? opts.MaxTokens,
            System = systemPrompt,
            Messages = conversationMessages,
            Tools = BuildTools(tools),
            Stream = true,
            OutputConfig = string.IsNullOrWhiteSpace(reasoningEffort) ? null : new() { Effort = reasoningEffort },
            Thinking = supportsAdaptiveThinking && !string.IsNullOrWhiteSpace(reasoningEffort) ? new() : null
        };

        var toolCallBuilders = new Dictionary<int, (string Id, string Name, StringBuilder Args)>();
        string? finishReason = null;
        var outputBlocks = new SortedDictionary<int, JsonObject>();
        var inputJson = new Dictionary<int, StringBuilder>();

        await foreach (var (eventType, data) in StreamRawAsync(request, opts, cancellationToken))
        {
            switch (eventType)
            {
                case "content_block_start":
                {
                    var parsed = JsonSerializer.Deserialize(data,
                        AnthropicJsonContext.Default.AnthropicContentBlockStartData);
                    if (parsed?.ContentBlock is { } block)
                    {
                        using var raw = JsonDocument.Parse(data);
                        outputBlocks[parsed.Index] = JsonNode.Parse(raw.RootElement.GetProperty("content_block").GetRawText())!.AsObject();
                        if (block.Type == "tool_use" && block.Id is not null && block.Name is not null)
                        {
                            toolCallBuilders[parsed.Index] = (block.Id, block.Name, new());
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
                        if (outputBlocks.TryGetValue(parsed.Index, out var output))
                        {
                            using var raw = JsonDocument.Parse(data);
                            var rawDelta = raw.RootElement.GetProperty("delta");
                            var property = delta.Type switch
                            {
                                "text_delta" => "text",
                                "thinking_delta" => "thinking",
                                "signature_delta" => "signature",
                                _ => null
                            };
                            if (property is not null && rawDelta.TryGetProperty(property, out var fragment))
                                output[property] = (output[property]?.GetValue<string>() ?? "") + fragment.GetString();
                            if (delta.Type == "input_json_delta")
                            {
                                if (!inputJson.TryGetValue(parsed.Index, out var inputBuilder))
                                    inputJson[parsed.Index] = inputBuilder = new StringBuilder();
                                inputBuilder.Append(delta.PartialJson);
                            }
                        }
                        if (delta.Type == "text_delta" && delta.Text is not null)
                            yield return new TextDelta(delta.Text);
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
                case "content_block_stop":
                {
                    var parsed = JsonSerializer.Deserialize(data,
                        AnthropicJsonContext.Default.AnthropicContentBlockStopData);
                    if (parsed is not null && inputJson.TryGetValue(parsed.Index, out var arguments))
                        outputBlocks[parsed.Index]["input"] = JsonNode.Parse(arguments.ToString());
                    break;
                }
                case "error":
                    throw new InvalidDataException("Anthropic reported a streaming error.");
                case "message_delta":
                {
                    var parsed = JsonSerializer.Deserialize(data,
                        AnthropicJsonContext.Default.AnthropicMessageDeltaData);
                    finishReason = parsed?.Delta?.StopReason;
                    break;
                }
            }
        }

        if (finishReason is null or "max_tokens")
            throw new InvalidDataException("Anthropic response ended before completion; increase the output token limit if exhausted.");
        conversationMessages.Add(new AnthropicMessage
        {
            Role = "assistant",
            Content = outputBlocks.Values.Select(block => block.Deserialize(
                AnthropicJsonContext.Default.AnthropicContentBlock)!).ToList()
        });
        LogStreamComplete(_logger, finishReason, toolCallBuilders.Count);
        yield return new Finished(finishReason)
        {
            Continuation = new AnthropicContinuation(opts, conversationMessages)
        };
    }

    private async IAsyncEnumerable<(string EventType, string Data)> StreamRawAsync(
        AnthropicMessagesRequest request,
        AnthropicOptions requestOptions,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var client = _httpClientFactory.CreateClient(HttpClientName);

        var jsonContent = JsonSerializer.SerializeToUtf8Bytes(request,
            AnthropicJsonContext.Default.AnthropicMessagesRequest);
        using var httpRequest = CreateRequest(HttpMethod.Post, requestOptions, "v1/messages");
        httpRequest.Content = new ByteArrayContent(jsonContent);
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
            blocks.Add(new() { Type = "text", Text = asst.Content });

        if (asst.ToolCalls is not null)
        {
            foreach (var tc in asst.ToolCalls)
            {
                blocks.Add(new()
                {
                    Type = "tool_use",
                    Id = tc.Id,
                    Name = tc.Name,
                    Input = JsonSerializer.Deserialize<JsonElement>(
                        string.IsNullOrEmpty(tc.Arguments) ? "{}" : tc.Arguments)
                });
            }
        }

        return new() { Role = "assistant", Content = blocks };
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
            messages.Add(new()
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

    private sealed record AnthropicContinuation(
        AnthropicOptions Options, List<AnthropicMessage> Messages) : IAIProviderContinuation;

    private static AnthropicOptions Snapshot(AnthropicOptions options) => new()
    {
        BaseUrl = options.BaseUrl,
        ApiKey = options.ApiKey,
        Model = options.Model,
        MaxTokens = options.MaxTokens,
        ApiVersion = options.ApiVersion
    };

    private AnthropicOptions GetConfiguredOptions()
    {
        var current = _getOptions();
        if (!IsValidConfiguration(current))
            throw new InvalidOperationException("Anthropic is not configured.");
        return current;
    }

    private static bool IsValidConfiguration(AnthropicOptions options)
        => Uri.TryCreate(options.BaseUrl, UriKind.Absolute, out var baseUri)
           && baseUri.Scheme is "http" or "https"
           && string.IsNullOrEmpty(baseUri.UserInfo)
           && string.IsNullOrEmpty(baseUri.Query)
           && string.IsNullOrEmpty(baseUri.Fragment)
           && !string.IsNullOrWhiteSpace(options.ApiKey)
           && !string.IsNullOrWhiteSpace(options.Model)
           && !string.IsNullOrWhiteSpace(options.ApiVersion)
           && options.MaxTokens > 0;

    private static HttpRequestMessage CreateRequest(
        HttpMethod method,
        AnthropicOptions options,
        string relativePath)
    {
        var baseUri = new Uri(options.BaseUrl.TrimEnd('/') + "/", UriKind.Absolute);
        var request = new HttpRequestMessage(method, new Uri(baseUri, relativePath));
        request.Headers.Add("x-api-key", options.ApiKey);
        request.Headers.Add("anthropic-version", options.ApiVersion);
        return request;
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "[Anthropic] Stream complete. stop_reason: {StopReason}, tool_calls: {ToolCallCount}")]
    private static partial void LogStreamComplete(ILogger logger, string? stopReason, int toolCallCount);
}
