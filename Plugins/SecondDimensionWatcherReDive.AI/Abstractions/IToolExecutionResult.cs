namespace SecondDimensionWatcherReDive.AI.Abstractions;

public interface IToolExecutionResult
{
    object? Result { get; }

    bool IsSuccess { get; }

    string SerializeResult();
}
