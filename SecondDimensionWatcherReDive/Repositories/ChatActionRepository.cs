using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using SecondDimensionWatcherReDive.Framework.DataRepository;
using SecondDimensionWatcherReDive.Models;
using DataPendingChatAction = SecondDimensionWatcherReDive.Framework.DataRepository.PendingChatAction;

namespace SecondDimensionWatcherReDive.Repositories;

public sealed class ChatActionRepository(ApplicationContext context) : IChatActionRepository
{
    public async Task AddAsync(
        PendingChatActionDraft action,
        CancellationToken cancellationToken)
    {
        if (action.ExpiresAt <= action.CreatedAt)
            throw new ArgumentException("A pending chat action must expire after it is created.", nameof(action));

        var entity = new ChatPendingAction
        {
            Id = action.Id,
            ConversationId = action.ConversationId,
            UserId = action.UserId,
            ToolCallId = action.ToolCallId,
            ToolName = action.ToolName,
            RiskLevel = action.RiskLevel,
            State = ChatActionState.Pending,
            ProtectedParameters = action.ProtectedParameters,
            ParameterHash = action.ParameterHash,
            ProtectedApprovalToken = action.ProtectedApprovalToken,
            ApprovalTokenHash = action.ApprovalTokenHash,
            ParameterSummary = action.ParameterSummary,
            ImpactSummary = action.ImpactSummary,
            IsReversible = action.IsReversible,
            CreatedAt = action.CreatedAt,
            ExpiresAt = action.ExpiresAt
        };
        entity.AuditEntries.Add(CreateAudit(entity, ChatActionAuditEvent.Requested, null, action.CreatedAt));
        await context.ChatPendingActions.AddAsync(entity, cancellationToken);
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task<DataPendingChatAction?> FindAsync(
        Guid actionId,
        Guid conversationId,
        Guid userId,
        CancellationToken cancellationToken)
    {
        var entity = await context.ChatPendingActions
            .AsNoTracking()
            .SingleOrDefaultAsync(action =>
                action.Id == actionId
                && action.ConversationId == conversationId
                && action.UserId == userId,
                cancellationToken);
        return entity is null ? null : ToRecord(entity);
    }

    public async Task<IReadOnlyList<DataPendingChatAction>> GetForConversationAsync(
        Guid conversationId,
        Guid userId,
        CancellationToken cancellationToken)
    {
        var entities = await context.ChatPendingActions
            .AsNoTracking()
            .Where(action => action.ConversationId == conversationId && action.UserId == userId)
            .OrderByDescending(action => action.CreatedAt)
            .ToListAsync(cancellationToken);
        return entities.Select(ToRecord).ToList();
    }

    public async Task<ChatActionClaimResult> TryClaimForExecutionAsync(
        Guid actionId,
        Guid conversationId,
        Guid userId,
        string approvalTokenHash,
        string parameterHash,
        bool destructiveConfirmed,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var action = await LoadBoundActionAsync(actionId, conversationId, userId, cancellationToken);
        if (action is null)
            return new(ChatActionClaimOutcome.NotFound);

        if (!await ConversationExistsAsync(conversationId, cancellationToken))
        {
            await AddAuditAsync(action, ChatActionAuditEvent.ApprovalDenied,
                "Conversation no longer exists", now, cancellationToken);
            return new(ChatActionClaimOutcome.ConversationMissing);
        }

        if (!FixedTimeEquals(action.ApprovalTokenHash, approvalTokenHash))
        {
            await AddAuditAsync(action, ChatActionAuditEvent.ApprovalDenied,
                "Approval token mismatch", now, cancellationToken);
            return new(ChatActionClaimOutcome.InvalidToken);
        }

        if (!FixedTimeEquals(action.ParameterHash, parameterHash))
        {
            await AddAuditAsync(action, ChatActionAuditEvent.ApprovalDenied,
                "Parameter hash mismatch", now, cancellationToken);
            return new(ChatActionClaimOutcome.ParameterMismatch);
        }

        if (action.State != ChatActionState.Pending)
            return new(ChatActionClaimOutcome.AlreadyProcessed, ToRecord(action));

        if (action.ExpiresAt <= now)
        {
            var expired = await TransitionPendingWithAuditAsync(
                action,
                ChatActionState.Expired,
                ChatActionAuditEvent.Expired,
                "Approval window expired",
                now,
                cancellationToken);
            return new(expired
                ? ChatActionClaimOutcome.Expired
                : ChatActionClaimOutcome.AlreadyProcessed);
        }

        if (action.RiskLevel == Framework.AI.ToolRiskLevel.Destructive && !destructiveConfirmed)
        {
            await AddAuditAsync(action, ChatActionAuditEvent.ApprovalDenied,
                "Destructive confirmation missing", now, cancellationToken);
            return new(ChatActionClaimOutcome.ConfirmationRequired, ToRecord(action));
        }

        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        var claimed = await context.ChatPendingActions
            .Where(candidate => candidate.Id == action.Id && candidate.State == ChatActionState.Pending)
            .ExecuteUpdateAsync(setters => setters
                    .SetProperty(candidate => candidate.State, ChatActionState.Executing)
                    .SetProperty(candidate => candidate.DecidedAt, now)
                    .SetProperty(candidate => candidate.ExecutionStartedAt, now)
                    .SetProperty(candidate => candidate.ProtectedApprovalToken, string.Empty),
                cancellationToken) == 1;
        if (!claimed)
            return new(ChatActionClaimOutcome.AlreadyProcessed);

        await context.ChatActionAudits.AddRangeAsync(
            [
                CreateAudit(action, ChatActionAuditEvent.Approved, "Approval token consumed", now),
                CreateAudit(action, ChatActionAuditEvent.ExecutionStarted, "Execution claimed", now)
            ],
            cancellationToken);
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        action.State = ChatActionState.Executing;
        action.DecidedAt = now;
        action.ExecutionStartedAt = now;
        action.ProtectedApprovalToken = string.Empty;
        return new(ChatActionClaimOutcome.Claimed, ToRecord(action));
    }

    public async Task<ChatActionRejectOutcome> TryRejectAsync(
        Guid actionId,
        Guid conversationId,
        Guid userId,
        string approvalTokenHash,
        string parameterHash,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var action = await LoadBoundActionAsync(actionId, conversationId, userId, cancellationToken);
        if (action is null)
            return ChatActionRejectOutcome.NotFound;

        if (!await ConversationExistsAsync(conversationId, cancellationToken))
        {
            await AddAuditAsync(action, ChatActionAuditEvent.ApprovalDenied,
                "Conversation no longer exists", now, cancellationToken);
            return ChatActionRejectOutcome.ConversationMissing;
        }
        if (!FixedTimeEquals(action.ApprovalTokenHash, approvalTokenHash))
        {
            await AddAuditAsync(action, ChatActionAuditEvent.ApprovalDenied,
                "Approval token mismatch", now, cancellationToken);
            return ChatActionRejectOutcome.InvalidToken;
        }
        if (!FixedTimeEquals(action.ParameterHash, parameterHash))
        {
            await AddAuditAsync(action, ChatActionAuditEvent.ApprovalDenied,
                "Parameter hash mismatch", now, cancellationToken);
            return ChatActionRejectOutcome.ParameterMismatch;
        }
        if (action.State != ChatActionState.Pending)
            return ChatActionRejectOutcome.AlreadyProcessed;
        if (action.ExpiresAt <= now)
        {
            var expired = await TransitionPendingWithAuditAsync(
                action,
                ChatActionState.Expired,
                ChatActionAuditEvent.Expired,
                "Approval window expired",
                now,
                cancellationToken);
            return expired ? ChatActionRejectOutcome.Expired : ChatActionRejectOutcome.AlreadyProcessed;
        }

        return await TransitionPendingWithAuditAsync(
            action,
            ChatActionState.Rejected,
            ChatActionAuditEvent.Rejected,
            "User rejected the action",
            now,
            cancellationToken)
            ? ChatActionRejectOutcome.Rejected
            : ChatActionRejectOutcome.AlreadyProcessed;
    }

    public async Task<bool> CompleteExecutionAsync(
        Guid actionId,
        bool succeeded,
        string toolResultJson,
        string? resultSummary,
        string? errorSummary,
        DateTimeOffset completedAt,
        CancellationToken cancellationToken)
    {
        var action = await context.ChatPendingActions
            .AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Id == actionId, cancellationToken);
        if (action is null)
            return false;

        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        var targetState = succeeded ? ChatActionState.Succeeded : ChatActionState.Failed;
        var updated = await context.ChatPendingActions
            .Where(candidate => candidate.Id == actionId && candidate.State == ChatActionState.Executing)
            .ExecuteUpdateAsync(setters => setters
                    .SetProperty(candidate => candidate.State, targetState)
                    .SetProperty(candidate => candidate.CompletedAt, completedAt)
                    .SetProperty(candidate => candidate.ResultSummary, resultSummary)
                    .SetProperty(candidate => candidate.ErrorSummary, errorSummary)
                    .SetProperty(candidate => candidate.ToolResultJson, toolResultJson),
                cancellationToken) == 1;
        if (!updated)
            return false;

        await ReplacePersistedToolResultAsync(action, toolResultJson, cancellationToken);
        await context.ChatActionAudits.AddAsync(
            CreateAudit(
                action,
                succeeded ? ChatActionAuditEvent.ExecutionSucceeded : ChatActionAuditEvent.ExecutionFailed,
                succeeded ? resultSummary : errorSummary,
                completedAt),
            cancellationToken);
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    public async Task<int> RecoverAbandonedExecutionsAsync(
        Guid conversationId,
        Guid userId,
        DateTimeOffset executionStartedBefore,
        string toolResultJson,
        string errorSummary,
        DateTimeOffset recoveredAt,
        CancellationToken cancellationToken)
    {
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        var abandoned = await context.ChatPendingActions
            .AsNoTracking()
            .Where(action =>
                action.ConversationId == conversationId
                && action.UserId == userId
                && action.State == ChatActionState.Executing
                && action.ExecutionStartedAt <= executionStartedBefore)
            .ToListAsync(cancellationToken);
        var recovered = 0;
        foreach (var action in abandoned)
        {
            var updated = await context.ChatPendingActions
                .Where(candidate =>
                    candidate.Id == action.Id
                    && candidate.State == ChatActionState.Executing
                    && candidate.ExecutionStartedAt <= executionStartedBefore)
                .ExecuteUpdateAsync(setters => setters
                        .SetProperty(candidate => candidate.State, ChatActionState.Failed)
                        .SetProperty(candidate => candidate.CompletedAt, recoveredAt)
                        .SetProperty(candidate => candidate.ResultSummary, (string?)null)
                        .SetProperty(candidate => candidate.ErrorSummary, errorSummary)
                        .SetProperty(candidate => candidate.ToolResultJson, toolResultJson),
                    cancellationToken) == 1;
            if (!updated)
                continue;

            recovered++;
            await ReplacePersistedToolResultAsync(action, toolResultJson, cancellationToken);
            await context.ChatActionAudits.AddAsync(
                CreateAudit(
                    action,
                    ChatActionAuditEvent.ExecutionFailed,
                    errorSummary,
                    recoveredAt),
                cancellationToken);
        }

        if (recovered > 0)
            await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return recovered;
    }

    public async Task<IReadOnlyList<ChatActionAuditEntry>> GetAuditAsync(
        Guid conversationId,
        Guid userId,
        CancellationToken cancellationToken)
    {
        return await context.ChatActionAudits
            .AsNoTracking()
            .Where(audit => audit.ConversationId == conversationId && audit.UserId == userId)
            .OrderByDescending(audit => audit.CreatedAt)
            .Select(audit => new ChatActionAuditEntry(
                audit.Id,
                audit.ActionId,
                audit.ConversationId,
                audit.UserId,
                audit.ToolName,
                audit.RiskLevel,
                audit.Event,
                audit.ParameterHash,
                audit.ParameterSummary,
                audit.Detail,
                audit.CreatedAt))
            .ToListAsync(cancellationToken);
    }

    private async Task<ChatPendingAction?> LoadBoundActionAsync(
        Guid actionId,
        Guid conversationId,
        Guid userId,
        CancellationToken cancellationToken) =>
        await context.ChatPendingActions
            .AsNoTracking()
            .SingleOrDefaultAsync(action =>
                action.Id == actionId
                && action.ConversationId == conversationId
                && action.UserId == userId,
                cancellationToken);

    private async Task<bool> ConversationExistsAsync(
        Guid conversationId,
        CancellationToken cancellationToken) =>
        await context.ChatConversations
            .AsNoTracking()
            .AnyAsync(conversation => conversation.Id == conversationId, cancellationToken);

    private async Task<bool> TransitionPendingWithAuditAsync(
        ChatPendingAction action,
        ChatActionState state,
        ChatActionAuditEvent auditEvent,
        string detail,
        DateTimeOffset decidedAt,
        CancellationToken cancellationToken)
    {
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        var updated = await context.ChatPendingActions
            .Where(candidate => candidate.Id == action.Id && candidate.State == ChatActionState.Pending)
            .ExecuteUpdateAsync(setters => setters
                    .SetProperty(candidate => candidate.State, state)
                    .SetProperty(candidate => candidate.DecidedAt, decidedAt)
                    .SetProperty(candidate => candidate.ProtectedApprovalToken, string.Empty),
                cancellationToken) == 1;
        if (!updated)
            return false;

        await context.ChatActionAudits.AddAsync(
            CreateAudit(action, auditEvent, detail, decidedAt), cancellationToken);
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    private async Task ReplacePersistedToolResultAsync(
        ChatPendingAction action,
        string toolResultJson,
        CancellationToken cancellationToken)
    {
        await context.ChatMessages
            .Where(message =>
                message.ConversationId == action.ConversationId
                && message.Role == "tool"
                && message.ToolCallId == action.ToolCallId)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(message => message.Content, toolResultJson),
                cancellationToken);
    }

    private async Task AddAuditAsync(
        ChatPendingAction action,
        ChatActionAuditEvent auditEvent,
        string? detail,
        DateTimeOffset createdAt,
        CancellationToken cancellationToken)
    {
        await context.ChatActionAudits.AddAsync(
            CreateAudit(action, auditEvent, detail, createdAt), cancellationToken);
        await context.SaveChangesAsync(cancellationToken);
    }

    private static ChatActionAudit CreateAudit(
        ChatPendingAction action,
        ChatActionAuditEvent auditEvent,
        string? detail,
        DateTimeOffset createdAt) => new()
        {
            ActionId = action.Id,
            ConversationId = action.ConversationId,
            UserId = action.UserId,
            ToolName = action.ToolName,
            RiskLevel = action.RiskLevel,
            Event = auditEvent,
            ParameterHash = action.ParameterHash,
            ParameterSummary = action.ParameterSummary,
            Detail = detail,
            CreatedAt = createdAt
        };

    private static bool FixedTimeEquals(string left, string right) =>
        CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(left),
            Encoding.UTF8.GetBytes(right));

    private static DataPendingChatAction ToRecord(ChatPendingAction action) => new(
        action.Id,
        action.ConversationId,
        action.UserId,
        action.ToolCallId,
        action.ToolName,
        action.RiskLevel,
        action.State,
        action.ProtectedParameters,
        action.ParameterHash,
        action.ProtectedApprovalToken,
        action.ApprovalTokenHash,
        action.ParameterSummary,
        action.ImpactSummary,
        action.IsReversible,
        action.CreatedAt,
        action.ExpiresAt,
        action.DecidedAt,
        action.ExecutionStartedAt,
        action.CompletedAt,
        action.ResultSummary,
        action.ErrorSummary,
        action.ToolResultJson);
}
