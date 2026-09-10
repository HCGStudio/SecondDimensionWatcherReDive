namespace SecondDimensionWatcherReDive.Framework.AI;

/// <summary>Provider and model overrides captured for a single execution.</summary>
public sealed record AIExecutionSelection(
    string? ProviderId = null,
    string? Model = null,
    string? ReasoningEffort = null);

/// <summary>
/// Carries a queued task's selection through its asynchronous work and DI scopes.
/// Each execution restores the previous value when it finishes.
/// </summary>
public static class AIExecutionContext
{
    private static readonly AsyncLocal<AIExecutionSelection?> Selection = new();

    public static AIExecutionSelection? Current => Selection.Value;

    public static IDisposable Push(AIExecutionSelection? selection)
    {
        var scope = new Scope(Selection.Value);
        Selection.Value = selection;
        return scope;
    }

    private sealed class Scope(AIExecutionSelection? previous) : IDisposable
    {
        public void Dispose() => Selection.Value = previous;
    }
}
