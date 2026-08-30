namespace SecondDimensionWatcherReDive.Framework.DataRepository;

public interface IChatRepository
{
    Task<IReadOnlyList<ChatConversationSummary>> GetConversationsAsync(
        Guid profileId,
        CancellationToken cancellationToken);
    Task<ChatConversationDetail?> GetConversationWithMessagesAsync(
        Guid id,
        Guid profileId,
        CancellationToken cancellationToken);
    Task<ChatConversationSummary> CreateConversationAsync(
        Guid profileId,
        string? title,
        CancellationToken cancellationToken);
    Task<bool> DeleteConversationAsync(
        Guid id,
        Guid profileId,
        CancellationToken cancellationToken);
    Task UpdateConversationTitleAsync(
        Guid id,
        Guid profileId,
        string title,
        CancellationToken cancellationToken);
    Task AddMessageAsync(
        Guid conversationId,
        Guid profileId,
        ChatMessageRecord message,
        CancellationToken cancellationToken);
    Task AddMessagesAsync(
        Guid conversationId,
        Guid profileId,
        IEnumerable<ChatMessageRecord> messages,
        CancellationToken cancellationToken);
    Task<IReadOnlyList<ChatMessageRecord>> GetMessagesAsync(
        Guid conversationId,
        Guid profileId,
        CancellationToken cancellationToken);
    Task<int> GetMessageCountAsync(
        Guid conversationId,
        Guid profileId,
        CancellationToken cancellationToken);
}
