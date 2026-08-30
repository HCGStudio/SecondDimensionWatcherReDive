using Microsoft.Extensions.DependencyInjection;
using SecondDimensionWatcherReDive.Chat.Tools;

namespace SecondDimensionWatcherReDive.Chat;

public static class ChatServiceExtensions
{
    public static IServiceCollection AddChat(this IServiceCollection services)
    {
        services.AddScoped<QueryAnimationsTool>();
        services.AddScoped<ManageFeedsTool>();
        services.AddScoped<QuerySeasonTool>();
        services.AddScoped<SubscribeBangumiTool>();
        services.AddScoped<ManageTasksTool>();
        services.AddScoped<ManageDownloadsTool>();
        services.AddScoped<QueryFilesTool>();
        services.AddScoped<IChatRawToolExecutorFactory, ChatRawToolExecutorFactory>();
        services.AddScoped<IChatToolActionPlanner, ChatToolActionPlanner>();
        services.AddScoped<IChatActionService, ChatActionService>();
        services.AddScoped<IConversationTitleGenerator, ConversationTitleGenerator>();
        return services;
    }
}
