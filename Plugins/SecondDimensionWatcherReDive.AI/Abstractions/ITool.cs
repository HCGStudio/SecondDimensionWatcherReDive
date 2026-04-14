using System.Text.Json;
using SecondDimensionWatcherReDive.AI.Models;

namespace SecondDimensionWatcherReDive.AI.Abstractions;

public interface ITool
{
    static abstract ToolDefinition Definition { get; }

    Task<IToolExecutionResult> ExecuteAsync(JsonElement arguments, CancellationToken cancellationToken);
}
