using SecondDimensionWatcherReDive.Framework.DataRepository;
using SecondDimensionWatcherReDive.Repositories;
namespace SecondDimensionWatcherReDive.Utils.LibraryCompletion;

public static class LibraryCompletionExtensions
{
    public static IServiceCollection AddLibraryCompletion(this IServiceCollection services)
    {
        services.AddScoped<ILibraryCompletionRepository, LibraryCompletionRepository>();
        services.AddScoped<EpisodeAirCalendarService>();
        services.AddScoped<EpisodeDownloadService>();
        services.AddScoped<LibraryCompletionService>();
        return services;
    }
}
