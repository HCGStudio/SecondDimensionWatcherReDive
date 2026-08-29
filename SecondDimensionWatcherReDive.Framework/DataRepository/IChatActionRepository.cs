using SecondDimensionWatcherReDive.Framework.AI;

namespace SecondDimensionWatcherReDive.Framework.DataRepository;

public enum ChatActionState
{
    Pending,
    Executing,
    Succeeded,
    Failed,
    Rejected,
    Expired
}

public enum ChatActionAuditEvent
{
    Requested,
    Approved,
    Rejected,
    Expired,
    ApprovalDenied,
    ExecutionStarted,
    ExecutionSucceeded,
    ExecutionFailed
}

public sealed record PendingChatActionDraft(
    Guid Id,
    Guid ConversationId,
    Guid UserId,
    string ToolCallId,
    string ToolName,
    ToolRiskLevel RiskLevel,
    string ProtectedParameters,
    string ParameterHash,
    string ProtectedApprovalToken,
    string ApprovalTokenHash,
    string ParameterSummary,
    string ImpactSummary,
    bool IsReversible,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt);

public sealed record PendingChatAction(
    Guid Id,
    Guid ConversationId,
    Guid UserId,
    string ToolCallId,
    string ToolName,
    ToolRiskLevel RiskLevel,
    ChatActionState State,
    string ProtectedParameters,
    string ParameterHash,
    string ProtectedApprovalToken,
    string ApprovalTokenHash,
    string ParameterSummary,
    string ImpactSummary,
    bool IsReversible,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt,
    DateTimeOffset? DecidedAt,
    DateTimeOffset? ExecutionStartedAt,
    DateTimeOffset? CompletedAt,
    string? ResultSummary,
    string? ErrorSummary);

public enum ChatActionClaimOutcome
{
    Claimed,
    NotFound,
    InvalidToken,
    ParameterMismatch,
    ConfirmationRequired,
    Expired,
    AlreadyProcessed,
    ConversationMissing
}

public sealed record ChatActionClaimResult(
    ChatActionClaimOutcome Outcome,
    PendingChatAction? Action = null);

public enum ChatActionRejectOutcome
{
    Rejected,
    NotFound,
    InvalidToken,
    ParameterMismatch,
    Expired,
    AlreadyProcessed,
    ConversationMissing
}

public sealed record ChatActionAuditEntry(
    long Id,
    Guid ActionId,
    Guid ConversationId,
    Guid UserId,
    string ToolName,
    ToolRiskLevel RiskLevel,
    ChatActionAuditEvent Event,
    string ParameterHash,
    string ParameterSummary,
    string? Detail,
    DateTimeOffset CreatedAt);

public interface IChatActionRepository
{
    Task AddAsync(PendingChatActionDraft action, CancellationToken cancellationToken);

    Task<PendingChatAction?> FindAsync(
        Guid actionId,
        Guid conversationId,
        Guid userId,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<PendingChatAction>> GetForConversationAsync(
        Guid conversationId,
        Guid userId,
        CancellationToken cancellationToken);

    Task<ChatActionClaimResult> TryClaimForExecutionAsync(
        Guid actionId,
        Guid conversationId,
        Guid userId,
        string approvalTokenHash,
        string parameterHash,
        bool destructiveConfirmed,
        DateTimeOffset now,
        CancellationToken cancellationToken);

    Task<ChatActionRejectOutcome> TryRejectAsync(
        Guid actionId,
        Guid conversationId,
        Guid userId,
        string approvalTokenHash,
        string parameterHash,
        DateTimeOffset now,
        CancellationToken cancellationToken);

    Task CompleteExecutionAsync(
        Guid actionId,
        bool succeeded,
        string? resultSummary,
        string? errorSummary,
        DateTimeOffset completedAt,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<ChatActionAuditEntry>> GetAuditAsync(
        Guid conversationId,
        Guid userId,
        CancellationToken cancellationToken);
}
