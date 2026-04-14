namespace SecondDimensionWatcherReDive.Framework.DataRepository;

public sealed record ChatConversationSummary(
    Guid Id,
    string? Title,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record ChatConversationDetail(
    Guid Id,
    string? Title,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<ChatMessageRecord> Messages);

public sealed record ChatMessageRecord(
    Guid Id,
    string Role,
    string? Content,
    string? ToolCallsJson,
    string? ToolCallId,
    string? ToolName,
    int Order,
    DateTimeOffset CreatedAt);
