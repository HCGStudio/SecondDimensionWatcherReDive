using Microsoft.EntityFrameworkCore;
using SecondDimensionWatcherReDive.Framework.DataRepository;
using SecondDimensionWatcherReDive.Models;

namespace SecondDimensionWatcherReDive.Repositories;

public class ChatRepository(ApplicationContext context) : IChatRepository
{
    public async Task<IReadOnlyList<ChatConversationSummary>> GetConversationsAsync(
        CancellationToken cancellationToken)
    {
        return await context.ChatConversations
            .AsNoTracking()
            .OrderByDescending(c => c.UpdatedAt)
            .Select(c => new ChatConversationSummary(c.Id, c.Title, c.CreatedAt, c.UpdatedAt))
            .ToListAsync(cancellationToken);
    }

    public async Task<ChatConversationDetail?> GetConversationWithMessagesAsync(
        Guid id, CancellationToken cancellationToken)
    {
        var conversation = await context.ChatConversations
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == id, cancellationToken);

        if (conversation is null) return null;

        var messages = await context.ChatMessages
            .AsNoTracking()
            .Where(m => m.ConversationId == id)
            .OrderBy(m => m.Order)
            .Select(m => new ChatMessageRecord(
                m.Id, m.Role, m.Content, m.ToolCallsJson,
                m.ToolCallId, m.ToolName, m.Order, m.CreatedAt))
            .ToListAsync(cancellationToken);
        messages = await OverlayCompletedToolResultsAsync(
            id, messages, cancellationToken);

        return new ChatConversationDetail(
            conversation.Id, conversation.Title,
            conversation.CreatedAt, conversation.UpdatedAt, messages);
    }

    public async Task<ChatConversationSummary> CreateConversationAsync(
        string? title, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.Now;
        var entity = new ChatConversation
        {
            Id = Guid.NewGuid(),
            Title = title,
            CreatedAt = now,
            UpdatedAt = now
        };

        context.ChatConversations.Add(entity);
        await context.SaveChangesAsync(cancellationToken);

        return new ChatConversationSummary(entity.Id, entity.Title, entity.CreatedAt, entity.UpdatedAt);
    }

    public async Task<bool> DeleteConversationAsync(Guid id, CancellationToken cancellationToken)
    {
        var entity = await context.ChatConversations.FindAsync([id], cancellationToken);
        if (entity is null) return false;

        context.ChatConversations.Remove(entity);
        await context.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task UpdateConversationTitleAsync(
        Guid id, string title, CancellationToken cancellationToken)
    {
        var entity = await context.ChatConversations.FindAsync([id], cancellationToken);
        if (entity is null) return;

        entity.Title = title;
        entity.UpdatedAt = DateTimeOffset.Now;
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task AddMessageAsync(
        Guid conversationId, ChatMessageRecord message, CancellationToken cancellationToken)
    {
        var entity = new ChatMessage
        {
            Id = message.Id,
            ConversationId = conversationId,
            Role = message.Role,
            Content = message.Content,
            ToolCallsJson = message.ToolCallsJson,
            ToolCallId = message.ToolCallId,
            ToolName = message.ToolName,
            Order = message.Order,
            CreatedAt = message.CreatedAt
        };

        context.ChatMessages.Add(entity);

        // Update conversation timestamp
        var conversation = await context.ChatConversations.FindAsync([conversationId], cancellationToken);
        if (conversation is not null)
            conversation.UpdatedAt = DateTimeOffset.Now;

        await context.SaveChangesAsync(cancellationToken);
        if (message.Role == "tool" && message.ToolCallId is not null)
        {
            await ReconcileStoredToolResultsAsync(
                conversationId, [message.ToolCallId], cancellationToken);
        }
    }

    public async Task AddMessagesAsync(
        Guid conversationId, IEnumerable<ChatMessageRecord> messages, CancellationToken cancellationToken)
    {
        var messageList = messages.ToList();
        foreach (var message in messageList)
        {
            context.ChatMessages.Add(new ChatMessage
            {
                Id = message.Id,
                ConversationId = conversationId,
                Role = message.Role,
                Content = message.Content,
                ToolCallsJson = message.ToolCallsJson,
                ToolCallId = message.ToolCallId,
                ToolName = message.ToolName,
                Order = message.Order,
                CreatedAt = message.CreatedAt
            });
        }

        var conversation = await context.ChatConversations.FindAsync([conversationId], cancellationToken);
        if (conversation is not null)
            conversation.UpdatedAt = DateTimeOffset.Now;

        await context.SaveChangesAsync(cancellationToken);
        var toolCallIds = messageList
            .Where(message => message.Role == "tool" && message.ToolCallId is not null)
            .Select(message => message.ToolCallId!)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (toolCallIds.Length > 0)
        {
            await ReconcileStoredToolResultsAsync(
                conversationId, toolCallIds, cancellationToken);
        }
    }

    public async Task<IReadOnlyList<ChatMessageRecord>> GetMessagesAsync(
        Guid conversationId, CancellationToken cancellationToken)
    {
        var messages = await context.ChatMessages
            .AsNoTracking()
            .Where(m => m.ConversationId == conversationId)
            .OrderBy(m => m.Order)
            .Select(m => new ChatMessageRecord(
                m.Id, m.Role, m.Content, m.ToolCallsJson,
                m.ToolCallId, m.ToolName, m.Order, m.CreatedAt))
            .ToListAsync(cancellationToken);
        return await OverlayCompletedToolResultsAsync(
            conversationId, messages, cancellationToken);
    }

    public async Task<int> GetMessageCountAsync(
        Guid conversationId, CancellationToken cancellationToken)
    {
        return await context.ChatMessages
            .CountAsync(m => m.ConversationId == conversationId, cancellationToken);
    }

    private async Task<List<ChatMessageRecord>> OverlayCompletedToolResultsAsync(
        Guid conversationId,
        List<ChatMessageRecord> messages,
        CancellationToken cancellationToken)
    {
        var toolCallIds = messages
            .Where(message => message.Role == "tool" && message.ToolCallId is not null)
            .Select(message => message.ToolCallId!)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (toolCallIds.Length == 0)
            return messages;

        var completedResults = await GetCompletedToolResultsAsync(
            conversationId, toolCallIds, cancellationToken);
        if (completedResults.Count == 0)
            return messages;

        return messages.Select(message =>
            message.Role == "tool"
            && message.ToolCallId is not null
            && completedResults.TryGetValue(message.ToolCallId, out var result)
                ? message with { Content = result }
                : message).ToList();
    }

    private async Task ReconcileStoredToolResultsAsync(
        Guid conversationId,
        IReadOnlyCollection<string> toolCallIds,
        CancellationToken cancellationToken)
    {
        var completedResults = await GetCompletedToolResultsAsync(
            conversationId, toolCallIds, cancellationToken);
        foreach (var (toolCallId, result) in completedResults)
        {
            await context.ChatMessages
                .Where(message =>
                    message.ConversationId == conversationId
                    && message.Role == "tool"
                    && message.ToolCallId == toolCallId
                    && message.Content != result)
                .ExecuteUpdateAsync(
                    setters => setters.SetProperty(message => message.Content, result),
                    cancellationToken);
        }
    }

    private async Task<Dictionary<string, string>> GetCompletedToolResultsAsync(
        Guid conversationId,
        IReadOnlyCollection<string> toolCallIds,
        CancellationToken cancellationToken)
    {
        var results = await context.ChatPendingActions
            .AsNoTracking()
            .Where(action =>
                action.ConversationId == conversationId
                && toolCallIds.Contains(action.ToolCallId)
                && action.ToolResultJson != null)
            .OrderByDescending(action => action.CompletedAt)
            .Select(action => new { action.ToolCallId, action.ToolResultJson })
            .ToListAsync(cancellationToken);

        return results
            .GroupBy(result => result.ToolCallId, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.First().ToolResultJson!,
                StringComparer.Ordinal);
    }
}
