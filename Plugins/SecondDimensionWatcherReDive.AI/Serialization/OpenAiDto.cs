using System.Text.Json;
using System.Text.Json.Serialization;

namespace SecondDimensionWatcherReDive.AI.Serialization;

// ── Request ──

internal sealed class OpenAiChatRequest
{
    public required string Model { get; init; }

    public required List<OpenAiMessage> Messages { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<OpenAiTool>? Tools { get; init; }

    public bool Stream { get; init; } = true;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? MaxTokens { get; init; }
}

internal sealed class OpenAiMessage
{
    public required string Role { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Content { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<OpenAiToolCallDto>? ToolCalls { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ToolCallId { get; init; }
}

internal sealed class OpenAiTool
{
    public string Type { get; init; } = "function";

    public required OpenAiFunctionDef Function { get; init; }
}

internal sealed class OpenAiFunctionDef
{
    public required string Name { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Description { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonElement? Parameters { get; init; }
}

internal sealed class OpenAiToolCallDto
{
    public required string Id { get; init; }

    public string Type { get; init; } = "function";

    public required OpenAiFunctionCall Function { get; init; }
}

internal sealed class OpenAiFunctionCall
{
    public required string Name { get; init; }

    public required string Arguments { get; init; }
}

// ── Streaming Response ──

internal sealed class OpenAiChatChunk
{
    public string? Id { get; set; }

    public List<OpenAiChoice>? Choices { get; set; }
}

internal sealed class OpenAiChoice
{
    public int Index { get; set; }

    public OpenAiDelta? Delta { get; set; }

    public string? FinishReason { get; set; }
}

internal sealed class OpenAiDelta
{
    public string? Role { get; set; }

    public string? Content { get; set; }

    public List<OpenAiToolCallChunk>? ToolCalls { get; set; }
}

internal sealed class OpenAiToolCallChunk
{
    public int Index { get; set; }

    public string? Id { get; set; }

    public string? Type { get; set; }

    public OpenAiFunctionCallChunk? Function { get; set; }
}

internal sealed class OpenAiFunctionCallChunk
{
    public string? Name { get; set; }

    public string? Arguments { get; set; }
}

// ── Models List ──

internal sealed class OpenAiModelsResponse
{
    public List<OpenAiModelDto>? Data { get; set; }
}

internal sealed class OpenAiModelDto
{
    public string? Id { get; set; }

    public string? OwnedBy { get; set; }
}
