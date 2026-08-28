using SecondDimensionWatcherReDive.AI.Configuration;

namespace SecondDimensionWatcherReDive.AI.Abstractions;

public interface IAIEngineStatus
{
    string Name { get; }

    bool IsConfigured { get; }
}

public interface IAIEngineBackend : IAIEngine, IAIEngineStatus
{
    AIEngineKind Kind { get; }
}
