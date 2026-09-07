using SecondDimensionWatcherReDive.AI.Abstractions;
using SecondDimensionWatcherReDive.AI.Models;
using SecondDimensionWatcherReDive.Chat.Tools;
using SecondDimensionWatcherReDive.Framework.AI;

namespace SecondDimensionWatcherReDive.Chat;

internal interface IChatRawToolExecutorFactory
{
    IToolExecutor Create();
}

internal sealed class ChatRawToolExecutorFactory(IServiceProvider serviceProvider)
    : IChatRawToolExecutorFactory
{
    public IToolExecutor Create() => new ToolExecutorBuilder(serviceProvider)
        .AddTool<QueryAnimationsTool>()
        .AddTool<ManageFeedsTool>()
        .AddTool<QuerySeasonTool>()
        .AddTool<SubscribeBangumiTool>()
        .AddTool<ManageTasksTool>()
        .AddTool<ManageDownloadsTool>()
        .AddTool<QueryFilesTool>()
        .Build();
}

internal sealed class ApprovalToolExecutor(
    IToolExecutor inner,
    IChatToolActionPlanner planner,
    IChatActionService actionService,
    Guid conversationId,
    Guid userId) : IToolExecutor
{
    private readonly IReadOnlyDictionary<string, ToolDefinition> _definitions =
        inner.ToolDefinitions.ToDictionary(definition => definition.Name, StringComparer.Ordinal);

    public IReadOnlyList<ToolDefinition> ToolDefinitions => inner.ToolDefinitions;

    public async Task<IToolResult> ExecuteAsync(
        ToolCall toolCall,
        CancellationToken cancellationToken)
    {
        if (!_definitions.TryGetValue(toolCall.Name, out var definition))
            return await inner.ExecuteAsync(toolCall, cancellationToken);

        var plan = await planner.PlanAsync(definition, toolCall, cancellationToken);
        if (plan.RiskLevel == ToolRiskLevel.ReadOnly)
            return await inner.ExecuteAsync(toolCall, cancellationToken);

        return await actionService.CreatePendingAsync(
            conversationId,
            userId,
            toolCall,
            plan,
            cancellationToken);
    }
}
