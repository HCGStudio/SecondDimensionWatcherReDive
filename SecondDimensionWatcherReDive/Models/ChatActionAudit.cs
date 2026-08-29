using SecondDimensionWatcherReDive.Framework.AI;
using SecondDimensionWatcherReDive.Framework.DataRepository;

namespace SecondDimensionWatcherReDive.Models;

public class ChatActionAudit
{
    public long Id { get; set; }
    public Guid ActionId { get; set; }
    public ChatPendingAction Action { get; set; } = null!;
    public Guid ConversationId { get; set; }
    public Guid UserId { get; set; }
    public string ToolName { get; set; } = null!;
    public ToolRiskLevel RiskLevel { get; set; }
    public ChatActionAuditEvent Event { get; set; }
    public string ParameterHash { get; set; } = null!;
    public string ParameterSummary { get; set; } = null!;
    public string? Detail { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
