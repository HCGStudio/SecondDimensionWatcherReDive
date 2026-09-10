using System.Runtime.CompilerServices;
using Microsoft.Extensions.Options;
using SecondDimensionWatcherReDive.AI.Abstractions;
using SecondDimensionWatcherReDive.AI.Configuration;
using SecondDimensionWatcherReDive.AI.Models;

namespace SecondDimensionWatcherReDive.AI.Engines;

public sealed class AIEngineRouter(
    IEnumerable<IAIEngineBackend> backends,
    IOptionsMonitor<AIOptions> options,
    AIProviderRegistry? providers = null,
    IOptionsMonitor<OpenAIOptions>? openAIOptions = null,
    IOptionsMonitor<AnthropicOptions>? anthropicOptions = null) : IAIEngine, IAIEngineStatus, IAISelectionValidator
{
    private readonly IReadOnlyDictionary<AIEngineKind, IAIEngineBackend> _backends =
        backends.ToDictionary(backend => backend.Kind);

    public string Name => options.CurrentValue.UsesNamedProviders
        ? "Providers" : GetCurrentBackend().Name;

    public bool IsConfigured => options.CurrentValue.UsesNamedProviders
        ? options.CurrentValue.Providers.Values.Any(AIProviderRegistry.IsConfigured)
        : GetCurrentBackend().IsConfigured;

    public Task<IReadOnlyList<AIModel>> GetAvailableModelsAsync(CancellationToken cancellationToken)
        => options.CurrentValue.UsesNamedProviders
            ? GetRegistry().GetAvailableModelsAsync(options.CurrentValue, cancellationToken)
            : GetConfiguredBackend().GetAvailableModelsAsync(cancellationToken);

    public async IAsyncEnumerable<IChatUpdate> ChatAsync(
        IReadOnlyList<IMessage> messages,
        ChatOptions? chatOptions,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        IAIEngineBackend backend;
        if (options.CurrentValue.UsesNamedProviders)
        {
            var selected = AIProviderRegistry.Select(options.CurrentValue, chatOptions?.ProviderId);
            chatOptions = AIProviderRegistry.ResolveSelection(selected.Key, selected.Value, chatOptions);
            backend = GetRegistry().Create(selected.Value);
        }
        else
        {
            ValidateSelection(chatOptions ?? new ChatOptions());
            backend = GetConfiguredBackend();
        }
        await foreach (var update in backend.ChatAsync(messages, chatOptions, cancellationToken))
            yield return update;
    }

    public void ValidateSelection(ChatOptions chatOptions, bool requiresTools = false)
    {
        if (chatOptions.MaxTokens is <= 0 || chatOptions.MaxToolRounds < 0)
            throw new ArgumentException("AI token and tool limits are invalid.");
        if (options.CurrentValue.UsesNamedProviders)
        {
            var selected = AIProviderRegistry.Select(options.CurrentValue, chatOptions.ProviderId);
            AIProviderRegistry.ResolveSelection(selected.Key, selected.Value, chatOptions, requiresTools);
            return;
        }
        var legacyId = options.CurrentValue.Engine == AIEngineKind.CodexAppServer
            ? "codex" : options.CurrentValue.Provider;
        if (!string.IsNullOrWhiteSpace(chatOptions.ProviderId) &&
            !string.Equals(chatOptions.ProviderId, legacyId, StringComparison.OrdinalIgnoreCase) &&
            !(options.CurrentValue.Engine == AIEngineKind.CodexAppServer && chatOptions.ProviderId == "codexAppServer"))
            throw new ArgumentException($"AI provider '{chatOptions.ProviderId}' is not configured.");
        if (!GetCurrentBackend().IsConfigured)
            throw new ArgumentException($"AI provider '{legacyId}' is not configured.");
        var isAnthropic = string.Equals(legacyId, "Anthropic", StringComparison.OrdinalIgnoreCase);
        var selectedModel = chatOptions.Model ?? (isAnthropic
            ? anthropicOptions?.CurrentValue.Model : openAIOptions?.CurrentValue.Model);
        if (selectedModel is { Length: > 0 } model && !string.IsNullOrWhiteSpace(chatOptions.ReasoningEffort) &&
            options.CurrentValue.Engine != AIEngineKind.CodexAppServer)
        {
            var protocol = isAnthropic ? AIProviderProtocol.Anthropic : AIProviderProtocol.OpenAIResponses;
            AIModelCapabilities.ValidateEffort(model, chatOptions.ReasoningEffort,
                AIModelCapabilities.GetReasoningEfforts(protocol, model));
        }
        if (!isAnthropic && options.CurrentValue.Engine != AIEngineKind.CodexAppServer &&
            openAIOptions?.CurrentValue is { ApiMode: OpenAIApiMode.ChatCompletions } openAI && selectedModel is not null)
            AIModelCapabilities.ValidateToolProtocol(AIProviderProtocol.OpenAIChatCompletions, openAI.BaseUrl,
                selectedModel, chatOptions.ReasoningEffort,
                requiresTools || chatOptions.ToolExecutor?.ToolDefinitions is { Count: > 0 } && chatOptions.MaxToolRounds > 0);
    }

    private AIProviderRegistry GetRegistry()
        => providers ?? throw new InvalidOperationException("AI provider registry is not registered.");

    private IAIEngineBackend GetConfiguredBackend()
    {
        var backend = GetCurrentBackend();
        if (!backend.IsConfigured)
            throw new InvalidOperationException($"AI engine '{backend.Name}' is not configured.");
        return backend;
    }

    private IAIEngineBackend GetCurrentBackend()
    {
        var kind = options.CurrentValue.Engine;
        if (_backends.TryGetValue(kind, out var backend))
            return backend;

        throw new InvalidOperationException($"AI engine '{kind}' is not registered.");
    }
}
