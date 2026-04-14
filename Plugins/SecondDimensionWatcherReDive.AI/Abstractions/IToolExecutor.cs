using SecondDimensionWatcherReDive.AI.Models;

namespace SecondDimensionWatcherReDive.AI.Abstractions;

public interface IToolExecutor
{
    IReadOnlyList<ToolDefinition> ToolDefinitions { get; }

    Task<IToolExecutionResult> ExecuteAsync(ToolCall toolCall, CancellationToken cancellationToken);
}
