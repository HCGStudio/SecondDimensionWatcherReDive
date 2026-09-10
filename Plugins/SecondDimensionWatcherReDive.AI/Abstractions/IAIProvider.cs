using SecondDimensionWatcherReDive.AI.Models;
using SecondDimensionWatcherReDive.Framework.AI;

namespace SecondDimensionWatcherReDive.AI.Abstractions;

public interface IAIProvider
{
    string ProviderName { get; }

    bool IsConfigured { get; }

    Task<IReadOnlyList<AIModel>> GetAvailableModelsAsync(CancellationToken cancellationToken);

    IAsyncEnumerable<IChatUpdate> StreamChatCompletionAsync(
        IReadOnlyList<IMessage> messages,
        IReadOnlyList<ToolDefinition>? tools,
        string? model,
        int? maxTokens,
        IAIProviderContinuation? continuation,
        CancellationToken cancellationToken);

    IAsyncEnumerable<IChatUpdate> StreamChatCompletionAsync(
        IReadOnlyList<IMessage> messages,
        IReadOnlyList<ToolDefinition>? tools,
        string? model,
        int? maxTokens,
        IAIProviderContinuation? continuation,
        string? reasoningEffort,
        CancellationToken cancellationToken)
        => string.IsNullOrWhiteSpace(reasoningEffort)
            ? StreamChatCompletionAsync(messages, tools, model, maxTokens, continuation, cancellationToken)
            : throw new ArgumentException($"Provider '{ProviderName}' does not support reasoning effort.");
}
