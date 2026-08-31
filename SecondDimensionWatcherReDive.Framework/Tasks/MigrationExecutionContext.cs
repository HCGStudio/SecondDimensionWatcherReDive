namespace SecondDimensionWatcherReDive.Framework.Tasks;

/// <summary>
///     Gives a migration its last durable checkpoint and a way to advance it.
/// </summary>
public sealed class MigrationExecutionContext(
    string? checkpoint,
    Func<string?, CancellationToken, Task> saveCheckpoint)
{
    public string? Checkpoint { get; private set; } = checkpoint;

    public async Task SaveCheckpointAsync(
        string? checkpoint,
        CancellationToken cancellationToken)
    {
        await saveCheckpoint(checkpoint, cancellationToken);
        Checkpoint = checkpoint;
    }
}

public enum MigrationFailurePolicy
{
    /// <summary>The failed migration is retryable, but this instance must not become ready.</summary>
    BlockStartup,

    /// <summary>The failure remains visible and retryable while startup may continue.</summary>
    ContinueStartup
}
