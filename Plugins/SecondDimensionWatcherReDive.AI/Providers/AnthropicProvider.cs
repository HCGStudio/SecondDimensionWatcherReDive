using System.Net.ServerSentEvents;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
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
        using var request = CreateRequest(HttpMethod.Get, opts, "v1/models");
        using var response = await client.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStreamAsync(cancellationToken);
        var result = await JsonSerializer.DeserializeAsync(json,
            AnthropicJsonContext.Default.AnthropicModelsResponse, cancellationToken);

        if (result?.Data is null) return [];

        return result.Data
            .Where(m => m.Id is not null)
            .Select(m => new AIModel(m.Id!, m.DisplayName ?? m.Id!, "Anthropic"))
            .ToList();
    }

    public async IAsyncEnumerable<IChatUpdate> StreamChatCompletionAsync(
        IReadOnlyList<IMessage> messages,
        IReadOnlyList<ToolDefinition>? tools,
        string? model,
        int? maxTokens,
        IAIProviderContinuation? continuation,
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

        var request = new AnthropicMessagesRequest
        {
            Model = model ?? opts.Model,
            MaxTokens = maxTokens ?? opts.MaxTokens,
            System = systemPrompt,
            Messages = conversationMessages,
            Tools = BuildTools(tools),
            Stream = true
        };

        var toolCallBuilders = new Dictionary<int, (string Id, string Name, StringBuilder Args)>();
        string? finishReason = null;

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
                case "message_delta":
                {
                    var parsed = JsonSerializer.Deserialize(data,
                        AnthropicJsonContext.Default.AnthropicMessageDeltaData);
                    finishReason = parsed?.Delta?.StopReason;
                    break;
                }
            }
        }

        LogStreamComplete(_logger, finishReason, toolCallBuilders.Count);
        yield return new Finished(finishReason)
        {
            Continuation = new AnthropicContinuation(opts)
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

    private sealed record AnthropicContinuation(AnthropicOptions Options) : IAIProviderContinuation;

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
