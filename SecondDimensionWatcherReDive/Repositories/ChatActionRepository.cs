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
            var expired = await TransitionPendingAsync(
                action.Id, ChatActionState.Expired, now, cancellationToken);
            if (expired)
                await AddAuditAsync(action, ChatActionAuditEvent.Expired,
                    "Approval window expired", now, cancellationToken);
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

        await AddAuditsAsync(action,
            [
                (ChatActionAuditEvent.Approved, "Approval token consumed"),
                (ChatActionAuditEvent.ExecutionStarted, "Execution claimed")
            ],
            now,
            cancellationToken);
        action.State = ChatActionState.Executing;
        action.DecidedAt = now;
        action.ExecutionStartedAt = now;
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
            var expired = await TransitionPendingAsync(
                action.Id, ChatActionState.Expired, now, cancellationToken);
            if (expired)
                await AddAuditAsync(action, ChatActionAuditEvent.Expired,
                    "Approval window expired", now, cancellationToken);
            return expired ? ChatActionRejectOutcome.Expired : ChatActionRejectOutcome.AlreadyProcessed;
        }

        var rejected = await TransitionPendingAsync(
            action.Id, ChatActionState.Rejected, now, cancellationToken);
        if (!rejected)
            return ChatActionRejectOutcome.AlreadyProcessed;

        await AddAuditAsync(action, ChatActionAuditEvent.Rejected,
            "User rejected the action", now, cancellationToken);
        return ChatActionRejectOutcome.Rejected;
    }

    public async Task CompleteExecutionAsync(
        Guid actionId,
        bool succeeded,
        string? resultSummary,
        string? errorSummary,
        DateTimeOffset completedAt,
        CancellationToken cancellationToken)
    {
        var action = await context.ChatPendingActions
            .AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Id == actionId, cancellationToken);
        if (action is null)
            return;

        var targetState = succeeded ? ChatActionState.Succeeded : ChatActionState.Failed;
        var updated = await context.ChatPendingActions
            .Where(candidate => candidate.Id == actionId && candidate.State == ChatActionState.Executing)
            .ExecuteUpdateAsync(setters => setters
                    .SetProperty(candidate => candidate.State, targetState)
                    .SetProperty(candidate => candidate.CompletedAt, completedAt)
                    .SetProperty(candidate => candidate.ResultSummary, resultSummary)
                    .SetProperty(candidate => candidate.ErrorSummary, errorSummary),
                cancellationToken) == 1;
        if (!updated)
            return;

        await AddAuditAsync(
            action,
            succeeded ? ChatActionAuditEvent.ExecutionSucceeded : ChatActionAuditEvent.ExecutionFailed,
            succeeded ? resultSummary : errorSummary,
            completedAt,
            cancellationToken);
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

    private async Task<bool> TransitionPendingAsync(
        Guid actionId,
        ChatActionState state,
        DateTimeOffset decidedAt,
        CancellationToken cancellationToken) =>
        await context.ChatPendingActions
            .Where(action => action.Id == actionId && action.State == ChatActionState.Pending)
            .ExecuteUpdateAsync(setters => setters
                    .SetProperty(action => action.State, state)
                    .SetProperty(action => action.DecidedAt, decidedAt)
                    .SetProperty(action => action.ProtectedApprovalToken, string.Empty),
                cancellationToken) == 1;

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

    private async Task AddAuditsAsync(
        ChatPendingAction action,
        IEnumerable<(ChatActionAuditEvent Event, string? Detail)> events,
        DateTimeOffset createdAt,
        CancellationToken cancellationToken)
    {
        await context.ChatActionAudits.AddRangeAsync(
            events.Select(item => CreateAudit(action, item.Event, item.Detail, createdAt)),
            cancellationToken);
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
        action.ErrorSummary);
}
