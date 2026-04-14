namespace SecondDimensionWatcherReDive.Framework.DataRepository;

public interface IChatRepository
{
    Task<IReadOnlyList<ChatConversationSummary>> GetConversationsAsync(CancellationToken cancellationToken);
    Task<ChatConversationDetail?> GetConversationWithMessagesAsync(Guid id, CancellationToken cancellationToken);
    Task<ChatConversationSummary> CreateConversationAsync(string? title, CancellationToken cancellationToken);
    Task<bool> DeleteConversationAsync(Guid id, CancellationToken cancellationToken);
    Task UpdateConversationTitleAsync(Guid id, string title, CancellationToken cancellationToken);
    Task AddMessageAsync(Guid conversationId, ChatMessageRecord message, CancellationToken cancellationToken);
    Task AddMessagesAsync(Guid conversationId, IEnumerable<ChatMessageRecord> messages, CancellationToken cancellationToken);
    Task<IReadOnlyList<ChatMessageRecord>> GetMessagesAsync(Guid conversationId, CancellationToken cancellationToken);
    Task<int> GetMessageCountAsync(Guid conversationId, CancellationToken cancellationToken);
}
