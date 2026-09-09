using SecondDimensionWatcherReDive.AI.Models;

namespace SecondDimensionWatcherReDive.AI.Abstractions;

public interface IAISelectionValidator
{
    void ValidateSelection(ChatOptions options, bool requiresTools = false);
}
