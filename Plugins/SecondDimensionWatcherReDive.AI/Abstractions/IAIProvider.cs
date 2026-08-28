using SecondDimensionWatcherReDive.AI.Models;
using SecondDimensionWatcherReDive.Framework.AI;

namespace SecondDimensionWatcherReDive.AI.Abstractions;

public interface IAIProvider
{
    string ProviderName { get; }

    Task<IReadOnlyList<AIModel>> GetAvailableModelsAsync(CancellationToken cancellationToken);

    IAsyncEnumerable<IChatUpdate> StreamChatCompletionAsync(
        IReadOnlyList<IMessage> messages,
        IReadOnlyList<ToolDefinition>? tools,
        string? model,
        int? maxTokens,
        IAIProviderContinuation? continuation,
        CancellationToken cancellationToken);
}
