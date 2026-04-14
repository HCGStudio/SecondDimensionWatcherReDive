using Microsoft.Extensions.DependencyInjection;
using SecondDimensionWatcherReDive.Framework.FileStore;
using SecondDimensionWatcherReDive.Framework.Plugin;

namespace SecondDimensionWatcherReDive.Plugin.FileRenamer;

public static class FileRenamerServiceExtensions
{
    public static IServiceCollection AddFileRenamer(this IServiceCollection services)
    {
        services.AddScoped<IFileRenamer, VideoFileRenamer>();
        services.AddSingleton<IPlugin, FileRenamerPlugin>();
        return services;
    }
}
