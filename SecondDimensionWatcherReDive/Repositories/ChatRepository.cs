using Microsoft.EntityFrameworkCore;
using SecondDimensionWatcherReDive.Framework.DataRepository;
using SecondDimensionWatcherReDive.Models;

namespace SecondDimensionWatcherReDive.Repositories;

public class ChatRepository(ApplicationContext context) : IChatRepository
{
    public async Task<IReadOnlyList<ChatConversationSummary>> GetConversationsAsync(
        Guid profileId,
        CancellationToken cancellationToken)
    {
        return await context.ChatConversations
            .AsNoTracking()
            .Where(c => c.ProfileId == profileId)
            .OrderByDescending(c => c.UpdatedAt)
            .Select(c => new ChatConversationSummary(c.Id, c.Title, c.CreatedAt, c.UpdatedAt))
            .ToListAsync(cancellationToken);
    }

    public async Task<ChatConversationDetail?> GetConversationWithMessagesAsync(
        Guid id,
        Guid profileId,
        CancellationToken cancellationToken)
    {
        var conversation = await context.ChatConversations
            .AsNoTracking()
            .FirstOrDefaultAsync(
                c => c.Id == id && c.ProfileId == profileId,
                cancellationToken);

        if (conversation is null) return null;

        var messages = await context.ChatMessages
            .AsNoTracking()
            .Where(m => m.ConversationId == id)
            .OrderBy(m => m.Order)
            .Select(m => new ChatMessageRecord(
                m.Id, m.Role, m.Content, m.ToolCallsJson,
                m.ToolCallId, m.ToolName, m.Order, m.CreatedAt))
            .ToListAsync(cancellationToken);

        return new ChatConversationDetail(
            conversation.Id, conversation.Title,
            conversation.CreatedAt, conversation.UpdatedAt, messages);
    }

    public async Task<ChatConversationSummary> CreateConversationAsync(
        Guid profileId,
        string? title,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.Now;
        var entity = new ChatConversation
        {
            Id = Guid.NewGuid(),
            ProfileId = profileId,
            Title = title,
            CreatedAt = now,
            UpdatedAt = now
        };

        context.ChatConversations.Add(entity);
        await context.SaveChangesAsync(cancellationToken);

        return new ChatConversationSummary(entity.Id, entity.Title, entity.CreatedAt, entity.UpdatedAt);
    }

    public async Task<bool> DeleteConversationAsync(
        Guid id,
        Guid profileId,
        CancellationToken cancellationToken)
    {
        var entity = await context.ChatConversations.FirstOrDefaultAsync(
            conversation => conversation.Id == id && conversation.ProfileId == profileId,
            cancellationToken);
        if (entity is null) return false;

        context.ChatConversations.Remove(entity);
        await context.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task UpdateConversationTitleAsync(
        Guid id,
        Guid profileId,
        string title,
        CancellationToken cancellationToken)
    {
        var entity = await context.ChatConversations.FirstOrDefaultAsync(
            conversation => conversation.Id == id && conversation.ProfileId == profileId,
            cancellationToken);
        if (entity is null) return;

        entity.Title = title;
        entity.UpdatedAt = DateTimeOffset.Now;
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task AddMessageAsync(
        Guid conversationId,
        Guid profileId,
        ChatMessageRecord message,
        CancellationToken cancellationToken)
    {
        var conversation = await context.ChatConversations.FirstOrDefaultAsync(
            candidate => candidate.Id == conversationId
                         && candidate.ProfileId == profileId,
            cancellationToken);
        if (conversation is null) return;

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
        conversation.UpdatedAt = DateTimeOffset.Now;

        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task AddMessagesAsync(
        Guid conversationId,
        Guid profileId,
        IEnumerable<ChatMessageRecord> messages,
        CancellationToken cancellationToken)
    {
        var conversation = await context.ChatConversations.FirstOrDefaultAsync(
            candidate => candidate.Id == conversationId
                         && candidate.ProfileId == profileId,
            cancellationToken);
        if (conversation is null) return;

        foreach (var message in messages)
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

        conversation.UpdatedAt = DateTimeOffset.Now;

        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ChatMessageRecord>> GetMessagesAsync(
        Guid conversationId,
        Guid profileId,
        CancellationToken cancellationToken)
    {
        return await context.ChatMessages
            .AsNoTracking()
            .Where(m => m.ConversationId == conversationId
                        && m.Conversation.ProfileId == profileId)
            .OrderBy(m => m.Order)
            .Select(m => new ChatMessageRecord(
                m.Id, m.Role, m.Content, m.ToolCallsJson,
                m.ToolCallId, m.ToolName, m.Order, m.CreatedAt))
            .ToListAsync(cancellationToken);
    }

    public async Task<int> GetMessageCountAsync(
        Guid conversationId,
        Guid profileId,
        CancellationToken cancellationToken)
    {
        return await context.ChatMessages
            .CountAsync(
                m => m.ConversationId == conversationId
                     && m.Conversation.ProfileId == profileId,
                cancellationToken);
    }
}
