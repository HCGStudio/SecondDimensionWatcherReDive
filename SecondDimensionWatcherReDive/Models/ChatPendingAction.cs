using SecondDimensionWatcherReDive.Framework.AI;
using SecondDimensionWatcherReDive.Framework.DataRepository;

namespace SecondDimensionWatcherReDive.Models;

public class ChatPendingAction
{
    public Guid Id { get; set; }
    public Guid ConversationId { get; set; }
    public Guid UserId { get; set; }
    public string ToolCallId { get; set; } = null!;
    public string ToolName { get; set; } = null!;
    public ToolRiskLevel RiskLevel { get; set; }
    public ChatActionState State { get; set; }
    public string ProtectedParameters { get; set; } = null!;
    public string ParameterHash { get; set; } = null!;
    public string ProtectedApprovalToken { get; set; } = null!;
    public string ApprovalTokenHash { get; set; } = null!;
    public string ParameterSummary { get; set; } = null!;
    public string ImpactSummary { get; set; } = null!;
    public bool IsReversible { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? DecidedAt { get; set; }
    public DateTimeOffset? ExecutionStartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public string? ResultSummary { get; set; }
    public string? ErrorSummary { get; set; }
    public string? ToolResultJson { get; set; }
    public ICollection<ChatActionAudit> AuditEntries { get; set; } = [];
}
