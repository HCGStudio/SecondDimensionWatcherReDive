using System.Text.Json.Serialization;

namespace SecondDimensionWatcherReDive.Chat.External;

// --- Request DTOs ---

internal sealed record SendMessageRequest(string Content, string? Model);
internal sealed record CreateConversationRequest(string? Title);
internal sealed record UpdateConversationRequest(string Title);
internal sealed record ApproveChatActionRequest(
    string ApprovalToken,
    string ParameterHash,
    bool ConfirmDestructive);
internal sealed record RejectChatActionRequest(
    string ApprovalToken,
    string ParameterHash);

// --- Response DTOs ---

internal sealed record ChatStatusResponse(bool AiEnabled, string? Provider);

internal sealed record ChatActionResponse(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("conversationId")] Guid ConversationId,
    [property: JsonPropertyName("toolCallId")] string ToolCallId,
    [property: JsonPropertyName("toolName")] string ToolName,
    [property: JsonPropertyName("riskLevel")] string RiskLevel,
    [property: JsonPropertyName("state")] string State,
    [property: JsonPropertyName("parameterHash")] string ParameterHash,
    [property: JsonPropertyName("parameterSummary")] string ParameterSummary,
    [property: JsonPropertyName("impactSummary")] string ImpactSummary,
    [property: JsonPropertyName("isReversible")] bool IsReversible,
    [property: JsonPropertyName("createdAt")] DateTimeOffset CreatedAt,
    [property: JsonPropertyName("expiresAt")] DateTimeOffset ExpiresAt,
    [property: JsonPropertyName("decidedAt")] DateTimeOffset? DecidedAt,
    [property: JsonPropertyName("completedAt")] DateTimeOffset? CompletedAt,
    [property: JsonPropertyName("resultSummary")] string? ResultSummary,
    [property: JsonPropertyName("errorSummary")] string? ErrorSummary,
    [property: JsonPropertyName("approvalToken")] string? ApprovalToken,
    [property: JsonPropertyName("toolResult")] string? ToolResult = null);

internal sealed record ChatActionDecisionResponse(
    string Outcome,
    ChatActionResponse? Action);

internal sealed record ChatActionAuditResponse(
    long Id,
    Guid ActionId,
    Guid ConversationId,
    string ToolName,
    string RiskLevel,
    string Event,
    string ParameterHash,
    string ParameterSummary,
    string? Detail,
    DateTimeOffset CreatedAt);

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

internal sealed record SseApprovalRequired(
    [property: JsonPropertyName("tool_call_id")] string ToolCallId,
    [property: JsonPropertyName("action")] ChatActionResponse Action);

internal sealed record SseFinished(
    [property: JsonPropertyName("stop_reason")] string? StopReason);

internal sealed record SseError(
    [property: JsonPropertyName("message")] string Message);

// --- Persisted tool call shape (serialized into ChatMessage.ToolCallsJson) ---

internal sealed record ToolCallRecord(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("arguments")] string Arguments);
