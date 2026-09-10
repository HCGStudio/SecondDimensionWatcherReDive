using SecondDimensionWatcherReDive.Framework.AI;

namespace SecondDimensionWatcherReDive.Framework.Tasks;

/// <summary>A task that consumes a provider/model selection for a manual execution.</summary>
public interface IAISelectableTask : IScheduledTask
{
    bool TryEnqueue(AIExecutionSelection selection);
}
