using System.Text.Json;

namespace SecondDimensionWatcherReDive.Framework.AI;

public interface ITool
{
    static abstract ToolDefinition Definition { get; }

    Task<IToolResult> ExecuteAsync(JsonElement arguments, CancellationToken cancellationToken);
}
