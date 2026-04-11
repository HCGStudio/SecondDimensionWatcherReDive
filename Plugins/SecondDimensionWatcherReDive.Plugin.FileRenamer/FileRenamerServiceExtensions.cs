using Microsoft.Extensions.DependencyInjection;
using SecondDimensionWatcherReDive.Framework.FileStore;

namespace SecondDimensionWatcherReDive.Plugin.FileRenamer;

public static class FileRenamerServiceExtensions
{
    public static IServiceCollection AddFileRenamer(this IServiceCollection services)
    {
        services.AddScoped<IFileRenamer, VideoFileRenamer>();
        return services;
    }
}
