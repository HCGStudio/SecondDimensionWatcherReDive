using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SecondDimensionWatcherReDive.AI.Abstractions;
using SecondDimensionWatcherReDive.AI.Configuration;
using SecondDimensionWatcherReDive.AI.Models;

namespace SecondDimensionWatcherReDive.AI.Engines;

public sealed partial class AIEngine : IAIEngineBackend
{
    private readonly IReadOnlyDictionary<string, IAIProvider> _providers;
    private readonly IOptionsMonitor<AIOptions>? _options;
    private readonly ILogger<AIEngine> _logger;

    public AIEngine(
        IEnumerable<IAIProvider> providers,
        IOptionsMonitor<AIOptions> options,
        ILogger<AIEngine> logger)
    {
        _providers = providers.ToDictionary(provider => provider.ProviderName,
            StringComparer.OrdinalIgnoreCase);
        _options = options;
        _logger = logger;
    }

    public AIEngine(IAIProvider provider, ILogger<AIEngine> logger)
    {
        _providers = new Dictionary<string, IAIProvider>(StringComparer.OrdinalIgnoreCase)
        {
            [provider.ProviderName] = provider
        };
        _logger = logger;
    }

    public AIEngineKind Kind => AIEngineKind.BuiltIn;

    public string Name => $"BuiltIn/{GetCurrentProvider().ProviderName}";

    public bool IsConfigured
    {
        get
        {
            try
            {
                return GetCurrentProvider().IsConfigured;
            }
            catch (InvalidOperationException)
            {
                return false;
            }
        }
    }

    public Task<IReadOnlyList<AIModel>> GetAvailableModelsAsync(CancellationToken cancellationToken)
        => GetConfiguredProvider().GetAvailableModelsAsync(cancellationToken);

    public async IAsyncEnumerable<IChatUpdate> ChatAsync(
        IReadOnlyList<IMessage> messages,
        ChatOptions? options,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var provider = GetConfiguredProvider();
        var maxToolRounds = options?.MaxToolRounds ?? 8;
        if (maxToolRounds < 0)
            throw new ArgumentOutOfRangeException(
                nameof(ChatOptions.MaxToolRounds), "MaxToolRounds cannot be negative.");

        var tools = options?.ToolExecutor?.ToolDefinitions;
        var conversation = new List<IMessage>(messages);
        IAIProviderContinuation? continuation = null;
        string? finishReason = null;

        // One initial model round plus, at most, one follow-up round for each executed tool round.
        for (var round = 0; round <= maxToolRounds; round++)
        {
            LogInferenceRound(_logger, provider.ProviderName, round + 1, maxToolRounds + 1);

            var textContent = new StringBuilder();
            var toolCallBuilders = new Dictionary<string, (string Name, StringBuilder Args)>();
            IAIProviderContinuation? nextContinuation = null;
            finishReason = null;

            // The last allowed model round is the final-answer round. Do not advertise tools there,
            // otherwise the model can legitimately request work whose result we cannot return.
            var canExecuteTools = round < maxToolRounds;
            var roundTools = canExecuteTools ? tools : null;
            await foreach (var update in provider.StreamChatCompletionAsync(
                               conversation, roundTools, options?.Model, options?.MaxTokens, continuation,
                               cancellationToken))
            {
                switch (update)
                {
                    case TextDelta td:
                        textContent.Append(td.Text);
                        yield return td;
                        break;
                    case ToolCallBegin tcb:
                        toolCallBuilders[tcb.Id] = (tcb.Name, new StringBuilder());
                        if (canExecuteTools)
                            yield return tcb;
                        break;
                    case ToolCallDelta tcd:
                        if (toolCallBuilders.TryGetValue(tcd.Id, out var builder))
                            builder.Args.Append(tcd.ArgumentsDelta);
                        if (canExecuteTools)
                            yield return tcd;
                        break;
                    case Finished f:
                        finishReason = f.StopReason;
                        nextContinuation = f.Continuation;
                        // Do not yield yet — only after the final round
                        break;
                }
            }

            continuation = nextContinuation;

            LogStreamComplete(_logger, provider.ProviderName, finishReason, toolCallBuilders.Count);

            if (toolCallBuilders.Count == 0) break;

            // A tool result needs one more model round to be consumed. Never execute calls from the
            // final allowed round and then silently discard their results (especially for tools with
            // side effects).
            if (round == maxToolRounds)
                throw new InvalidOperationException(
                    $"AI tool-call limit reached after {maxToolRounds} tool rounds.");

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
                    LogToolCall(_logger, provider.ProviderName, toolCall.Name);
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

    private IAIProvider GetConfiguredProvider()
    {
        var provider = GetCurrentProvider();
        if (!provider.IsConfigured)
            throw new InvalidOperationException($"AI provider '{provider.ProviderName}' is not configured.");
        return provider;
    }

    private IAIProvider GetCurrentProvider()
    {
        if (_options is null)
            return _providers.Values.Single();

        var providerName = _options.CurrentValue.Provider;
        if (_providers.TryGetValue(providerName, out var provider))
            return provider;

        throw new InvalidOperationException(
            $"Unknown AI provider '{providerName}'. Expected one of: {string.Join(", ", _providers.Keys)}.");
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
        Message = "[{Provider}] Tool call: {ToolName}")]
    private static partial void LogToolCall(
        ILogger logger, string provider, string toolName);
}
