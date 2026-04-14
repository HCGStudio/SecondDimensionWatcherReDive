using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using SecondDimensionWatcherReDive.AI.Abstractions;

namespace SecondDimensionWatcherReDive.AI.Models;

/// <summary>
///     Generic tool execution result with AOT-safe serialization via JsonSerializerContext.
/// </summary>
public sealed class ToolResult<T>(T? value, bool isSuccess, JsonTypeInfo<T> typeInfo) : IToolExecutionResult
{
    public T? Value => value;

    public object? Result => value;

    public bool IsSuccess => isSuccess;

    public string SerializeResult() =>
        value is null ? "null" : JsonSerializer.Serialize(value, typeInfo);
}

/// <summary>
///     Tool result wrapping an already-serialized JSON string.
/// </summary>
public sealed class ToolStringResult(string json, bool isSuccess = true) : IToolExecutionResult
{
    public object? Result => json;

    public bool IsSuccess => isSuccess;

    public string SerializeResult() => json;
}

/// <summary>
///     Tool error result.
/// </summary>
public sealed class ToolErrorResult(string error) : IToolExecutionResult
{
    public string Error => error;

    public object? Result => error;

    public bool IsSuccess => false;

    public string SerializeResult() =>
        $"{{\"error\":{JsonSerializer.Serialize(error)}}}";
}
