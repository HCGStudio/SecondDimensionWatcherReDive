using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using SecondDimensionWatcherReDive.Framework.AI;

namespace SecondDimensionWatcherReDive.AI.Models;

public sealed record ToolSuccessResult<T> : IToolResult
{
    private readonly JsonTypeInfo<T> _resultTypeInfo;

    public ToolSuccessResult(T result, JsonTypeInfo<T> resultTypeInfo)
    {
        Result = result;
        _resultTypeInfo = resultTypeInfo;
    }

    public ToolSuccessResult(T result)
        : this(result, (JsonTypeInfo<T>?)ToolJsonContext.Default.GetTypeInfo(typeof(T))
            ?? throw new NotSupportedException($"Tool result '{typeof(T)}' requires source-generated JSON metadata."))
    {
    }

    public T Result { get; }
    object? IToolResult.Result => Result;
    public bool IsSuccess => true;

    public JsonElement SerializeToElement() => JsonSerializer.SerializeToElement(
        new ToolSuccessPayload(JsonSerializer.SerializeToElement(Result, _resultTypeInfo), IsSuccess),
        ToolJsonContext.Default.ToolSuccessPayload);
}

public sealed record ToolFailureResult(string Error) : IToolResult
{
    public object? Result => Error;
    public bool IsSuccess => false;

    public JsonElement SerializeToElement() => JsonSerializer.SerializeToElement(
        new ToolFailurePayload(Error, Error, IsSuccess), ToolJsonContext.Default.ToolFailurePayload);
}

internal sealed record ToolSuccessPayload(JsonElement Result, bool IsSuccess);
internal sealed record ToolFailurePayload(string Error, string Result, bool IsSuccess);
