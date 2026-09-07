using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using SecondDimensionWatcherReDive.Framework.DataRepository;
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
    public IToolExecutor Create()
    {
        var user = serviceProvider.GetService<IHttpContextAccessor>()?.HttpContext?.User;
        IToolExecutorBuilder builder = new ToolExecutorBuilder(serviceProvider)
            .AddTool<QueryAnimationsTool>()
            .AddTool<QuerySeasonTool>()
            .AddTool<QueryFilesTool>();
        if (user?.IsInRole(nameof(UserRole.Admin)) == true || user?.IsInRole(nameof(UserRole.Member)) == true)
            builder = builder.AddTool<ManageFeedsTool>()
                .AddTool<SubscribeBangumiTool>()
                .AddTool<ManageDownloadsTool>();
        if (user?.IsInRole(nameof(UserRole.Admin)) == true)
            builder = builder.AddTool<ManageTasksTool>();
        return builder.Build();
    }
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
