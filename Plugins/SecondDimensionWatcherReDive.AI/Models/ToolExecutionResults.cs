using SecondDimensionWatcherReDive.Framework.AI;

namespace SecondDimensionWatcherReDive.AI.Models;

public sealed record ToolSuccessResult<T>(T Result) : IToolResult
{
    object? IToolResult.Result => Result;

    public bool IsSuccess => true;
}

public sealed record ToolFailureResult(string Error) : IToolResult
{
    public object? Result => Error;

    public bool IsSuccess => false;
}
