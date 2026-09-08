using SecondDimensionWatcherReDive.Framework.DataRepository;
using SecondDimensionWatcherReDive.Repositories;
namespace SecondDimensionWatcherReDive.Utils.LibraryCompletion;

public static class LibraryCompletionExtensions
{
    public static IServiceCollection AddMultiSourceSubscriptions(this IServiceCollection services)
    {
        services.AddScoped<IMultiSourceSubscriptionRepository, MultiSourceSubscriptionRepository>();
        services.AddScoped<MultiSourceCoordinator>();
        services.AddHostedService<MultiSourceBackgroundService>();
        return services;
    }

    public static IServiceCollection AddLibraryCompletion(this IServiceCollection services)
    {
        services.AddScoped<ILibraryCompletionRepository, LibraryCompletionRepository>();
        services.AddScoped<EpisodeAirCalendarService>();
        services.AddScoped<EpisodeDownloadService>();
        services.AddScoped<LibraryCompletionService>();
        return services;
    }
}
