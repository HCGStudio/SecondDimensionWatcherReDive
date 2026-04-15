using System.Text.Json;
using System.Text.Json.Serialization;

namespace SecondDimensionWatcherReDive.AI.External;

// ── Request ──

internal sealed class AnthropicMessagesRequest
{
    public required string Model { get; init; }

    public int MaxTokens { get; init; } = 1024;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? System { get; init; }

    public required List<AnthropicMessage> Messages { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<AnthropicTool>? Tools { get; init; }

    public bool Stream { get; init; } = true;
}

internal sealed class AnthropicMessage
{
    public required string Role { get; init; }

    public required List<AnthropicContentBlock> Content { get; init; }
}

internal sealed class AnthropicContentBlock
{
    public required string Type { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Text { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Id { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Name { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonElement? Input { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ToolUseId { get; init; }

    /// <summary>Tool result content (for "tool_result" type blocks). Maps to "content" in JSON.</summary>
    [JsonPropertyName("content")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ResultContent { get; init; }
}

internal sealed class AnthropicTool
{
    public required string Name { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Description { get; init; }

    public required JsonElement InputSchema { get; init; }
}

// ── Streaming Response Events ──

internal sealed class AnthropicMessageStartData
{
    public string? Type { get; set; }

    public AnthropicMessageInfo? Message { get; set; }
}

internal sealed class AnthropicMessageInfo
{
    public string? Id { get; set; }

    public string? Role { get; set; }

    public string? Model { get; set; }

    public string? StopReason { get; set; }
}

internal sealed class AnthropicContentBlockStartData
{
    public string? Type { get; set; }

    public int Index { get; set; }

    public AnthropicContentBlockInfo? ContentBlock { get; set; }
}

internal sealed class AnthropicContentBlockInfo
{
    public string? Type { get; set; }

    public string? Text { get; set; }

    public string? Id { get; set; }

    public string? Name { get; set; }
}

internal sealed class AnthropicContentBlockDeltaData
{
    public string? Type { get; set; }

    public int Index { get; set; }

    public AnthropicDelta? Delta { get; set; }
}

internal sealed class AnthropicDelta
{
    public string? Type { get; set; }

    public string? Text { get; set; }

    public string? PartialJson { get; set; }

    public string? StopReason { get; set; }
}

internal sealed class AnthropicContentBlockStopData
{
    public string? Type { get; set; }

    public int Index { get; set; }
}

internal sealed class AnthropicMessageDeltaData
{
    public string? Type { get; set; }

    public AnthropicDelta? Delta { get; set; }
}

// ── Models List ──

internal sealed class AnthropicModelsResponse
{
    public List<AnthropicModelEntry>? Data { get; set; }

    public bool HasMore { get; set; }
}

internal sealed class AnthropicModelEntry
{
    public string? Id { get; set; }

    public string? DisplayName { get; set; }

    public string? Type { get; set; }
}
