using System.Text.Json.Serialization;

namespace SecondDimensionWatcherReDive.Chat.External;

// --- Request DTOs ---

internal sealed record SendMessageRequest(string Content, string? Model);
internal sealed record CreateConversationRequest(string? Title);
internal sealed record UpdateConversationRequest(string Title);

// --- Response DTOs ---

internal sealed record ChatStatusResponse(bool AiEnabled, string? Provider);

// --- SSE event data DTOs ---

internal sealed record SseTextDelta(
    [property: JsonPropertyName("text")] string Text);

internal sealed record SseToolCallBegin(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("name")] string Name);

internal sealed record SseToolCallDelta(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("arguments_delta")] string ArgumentsDelta);

internal sealed record SseToolResult(
    [property: JsonPropertyName("tool_call_id")] string ToolCallId,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("result")] string Result);

internal sealed record SseFinished(
    [property: JsonPropertyName("stop_reason")] string? StopReason);

internal sealed record SseError(
    [property: JsonPropertyName("message")] string Message);

// --- Persisted tool call shape (serialized into ChatMessage.ToolCallsJson) ---

internal sealed record ToolCallRecord(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("arguments")] string Arguments);
