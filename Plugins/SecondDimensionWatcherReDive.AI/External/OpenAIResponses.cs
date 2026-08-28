using System.Text.Json;
using System.Text.Json.Serialization;

namespace SecondDimensionWatcherReDive.AI.External;

// ── Request (POST /responses) ──

internal sealed class OpenAIResponsesRequest
{
    public required string Model { get; init; }

    public required List<JsonElement> Input { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Instructions { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<OpenAIResponsesTool>? Tools { get; init; }

    public bool Stream { get; init; } = true;

    /// <summary>Execute at most one application tool per model round.</summary>
    public bool ParallelToolCalls { get; init; }

    /// <summary>Fail explicitly instead of silently dropping the oldest conversation items.</summary>
    public string Truncation { get; init; } = "disabled";

    /// <summary>
    ///     Keep the exchange stateless. Complete output items are carried locally between tool rounds.
    /// </summary>
    public bool Store { get; init; }

    /// <summary>
    ///     Required to carry reasoning items locally when <see cref="Store" /> is false.
    /// </summary>
    public List<string> Include { get; init; } = ["reasoning.encrypted_content"];

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? MaxOutputTokens { get; init; }
}

/// <summary>
///     Superset of the message, function_call, and function_call_output input item shapes.
/// </summary>
internal sealed class OpenAIResponsesInputItem
{
    public required string Type { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Role { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<OpenAIResponsesContentPart>? Content { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Phase { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? CallId { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Name { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Arguments { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Output { get; init; }
}

internal sealed class OpenAIResponsesContentPart
{
    public required string Type { get; init; }

    public required string Text { get; init; }
}

/// <summary>Responses function tools use a flat shape rather than a nested function object.</summary>
internal sealed class OpenAIResponsesTool
{
    public string Type { get; init; } = "function";

    public required string Name { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Description { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonElement? Parameters { get; init; }

    /// <summary>
    ///     Existing generated schemas are not yet normalized for every strict-mode constraint.
    /// </summary>
    public bool Strict { get; init; }
}

// ── Streaming events ──

/// <summary>A superset DTO for the Responses streaming events consumed by the provider.</summary>
internal sealed class OpenAIResponsesStreamEvent
{
    public string? Type { get; set; }

    public int OutputIndex { get; set; }

    public string? ItemId { get; set; }

    public string? Delta { get; set; }

    public string? Arguments { get; set; }

    public OpenAIResponsesOutputItem? Item { get; set; }

    public OpenAIResponsesResponse? Response { get; set; }

    public string? Code { get; set; }

    public string? Message { get; set; }

    public string? Param { get; set; }
}

internal sealed class OpenAIResponsesOutputItem
{
    public string? Type { get; set; }

    /// <summary>Output item id (for example, fc_...).</summary>
    public string? Id { get; set; }

    /// <summary>Logical call id that function_call_output must reference.</summary>
    public string? CallId { get; set; }

    public string? Name { get; set; }

    public string? Arguments { get; set; }

    public string? Status { get; set; }
}

internal sealed class OpenAIResponsesResponse
{
    public string? Id { get; set; }

    public string? Status { get; set; }

    public OpenAIResponsesIncompleteDetails? IncompleteDetails { get; set; }

    /// <summary>
    ///     Kept as raw JSON because every output item, including encrypted reasoning and phase data,
    ///     must be replayed byte-for-shape in a stateless tool continuation.
    /// </summary>
    public List<JsonElement>? Output { get; set; }

    public OpenAIResponsesError? Error { get; set; }
}

internal sealed class OpenAIResponsesIncompleteDetails
{
    public string? Reason { get; set; }
}

internal sealed class OpenAIResponsesError
{
    public string? Message { get; set; }

    public string? Code { get; set; }
}
