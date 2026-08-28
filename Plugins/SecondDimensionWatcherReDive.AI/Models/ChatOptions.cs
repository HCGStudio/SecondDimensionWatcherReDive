using SecondDimensionWatcherReDive.AI.Abstractions;

namespace SecondDimensionWatcherReDive.AI.Models;

public sealed class ChatOptions
{
    public IToolExecutor? ToolExecutor { get; init; }

    /// <summary>Maximum number of tool-execution rounds before the final model response.</summary>
    public int MaxToolRounds { get; init; } = 8;

    public int? MaxTokens { get; init; }

    /// <summary>
    ///     Optional model override. If set, the engine uses this model instead of the configured default.
    /// </summary>
    public string? Model { get; init; }
}
