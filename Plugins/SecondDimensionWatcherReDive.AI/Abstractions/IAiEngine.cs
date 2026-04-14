using SecondDimensionWatcherReDive.AI.Models;

namespace SecondDimensionWatcherReDive.AI.Abstractions;

public interface IAiEngine
{
    Task<IReadOnlyList<AiModel>> GetAvailableModelsAsync(CancellationToken cancellationToken);

    IAsyncEnumerable<IChatUpdate> ChatAsync(
        IReadOnlyList<IMessage> messages,
        ChatOptions? options,
        CancellationToken cancellationToken);
}
