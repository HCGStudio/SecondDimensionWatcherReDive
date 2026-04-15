using System.Text.Json;
using System.Text.Json.Serialization;

namespace SecondDimensionWatcherReDive.AI.External;

// ── Request ──

internal sealed class OpenAIChatRequest
{
    public required string Model { get; init; }

    public required List<OpenAIMessage> Messages { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<OpenAITool>? Tools { get; init; }

    public bool Stream { get; init; } = true;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? MaxTokens { get; init; }
}

internal sealed class OpenAIMessage
{
    public required string Role { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Content { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<OpenAIToolCall>? ToolCalls { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ToolCallId { get; init; }
}

internal sealed class OpenAITool
{
    public string Type { get; init; } = "function";

    public required OpenAIFunctionDef Function { get; init; }
}

internal sealed class OpenAIFunctionDef
{
    public required string Name { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Description { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonElement? Parameters { get; init; }
}

internal sealed class OpenAIToolCall
{
    public required string Id { get; init; }

    public string Type { get; init; } = "function";

    public required OpenAIFunctionCall Function { get; init; }
}

internal sealed class OpenAIFunctionCall
{
    public required string Name { get; init; }

    public required string Arguments { get; init; }
}

// ── Streaming Response ──

internal sealed class OpenAIChatChunk
{
    public string? Id { get; set; }

    public List<OpenAIChoice>? Choices { get; set; }
}

internal sealed class OpenAIChoice
{
    public int Index { get; set; }

    public OpenAIDelta? Delta { get; set; }

    public string? FinishReason { get; set; }
}

internal sealed class OpenAIDelta
{
    public string? Role { get; set; }

    public string? Content { get; set; }

    public List<OpenAIToolCallChunk>? ToolCalls { get; set; }
}

internal sealed class OpenAIToolCallChunk
{
    public int Index { get; set; }

    public string? Id { get; set; }

    public string? Type { get; set; }

    public OpenAIFunctionCallChunk? Function { get; set; }
}

internal sealed class OpenAIFunctionCallChunk
{
    public string? Name { get; set; }

    public string? Arguments { get; set; }
}

// ── Models List ──

internal sealed class OpenAIModelsResponse
{
    public List<OpenAIModelEntry>? Data { get; set; }
}

internal sealed class OpenAIModelEntry
{
    public string? Id { get; set; }

    public string? OwnedBy { get; set; }
}
