using SecondDimensionWatcherReDive.AI.Models;
using SecondDimensionWatcherReDive.Framework.AI;

namespace SecondDimensionWatcherReDive.AI.Abstractions;

public interface IToolExecutor
{
    IReadOnlyList<ToolDefinition> ToolDefinitions { get; }

    Task<IToolResult> ExecuteAsync(ToolCall toolCall, CancellationToken cancellationToken);
}
