namespace SecondDimensionWatcherReDive.AI.Models;

public sealed class ChatOptions
{
    public IReadOnlyList<ToolDefinition>? Tools { get; init; }

    public Func<ToolCall, CancellationToken, Task<string>>? ToolExecutor { get; init; }

    public int MaxToolRounds { get; init; } = 8;

    public int? MaxTokens { get; init; }
}
