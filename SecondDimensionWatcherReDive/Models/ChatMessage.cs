namespace SecondDimensionWatcherReDive.Models;

public class ChatMessage
{
    public Guid Id { get; set; }
    public Guid ConversationId { get; set; }
    public ChatConversation Conversation { get; set; } = null!;
    public string Role { get; set; } = null!;
    public string? Content { get; set; }
    public string? ToolCallsJson { get; set; }
    public string? ToolCallId { get; set; }
    public string? ToolName { get; set; }
    public int Order { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
