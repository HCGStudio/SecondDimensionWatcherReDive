namespace SecondDimensionWatcherReDive.Framework.AI;

public interface IToolResult
{
    object? Result { get; }

    bool IsSuccess { get; }
}
