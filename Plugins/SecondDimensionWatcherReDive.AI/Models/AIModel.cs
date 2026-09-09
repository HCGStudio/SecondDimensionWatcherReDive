namespace SecondDimensionWatcherReDive.AI.Models;

public sealed record AIModel(string Id, string Name, string Provider)
{
    public string? ProviderId { get; init; }

    public IReadOnlyList<string> ReasoningEfforts { get; init; } = [];
}
