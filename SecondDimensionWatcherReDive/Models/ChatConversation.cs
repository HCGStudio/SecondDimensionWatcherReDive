namespace SecondDimensionWatcherReDive.Models;

public class ChatConversation
{
    public Guid Id { get; set; }
    public string? Title { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public ICollection<ChatMessage> Messages { get; set; } = [];
}
