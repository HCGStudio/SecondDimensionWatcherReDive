namespace SecondDimensionWatcherReDive.Framework.AI;

/// <summary>
///     Declares the highest risk of an AI tool. Chat-facing callers may classify a
///     specific action at a lower level, but must never exceed this declaration.
/// </summary>
public enum ToolRiskLevel
{
    ReadOnly,
    Mutating,
    Destructive
}
