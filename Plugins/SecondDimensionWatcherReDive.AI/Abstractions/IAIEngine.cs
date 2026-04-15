using SecondDimensionWatcherReDive.AI.Models;

namespace SecondDimensionWatcherReDive.AI.Abstractions;

public interface IAIEngine
{
    Task<IReadOnlyList<AIModel>> GetAvailableModelsAsync(CancellationToken cancellationToken);

    IAsyncEnumerable<IChatUpdate> ChatAsync(
        IReadOnlyList<IMessage> messages,
        ChatOptions? options,
        CancellationToken cancellationToken);
}
