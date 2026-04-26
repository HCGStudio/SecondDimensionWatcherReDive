using Microsoft.Extensions.DependencyInjection;
using SecondDimensionWatcherReDive.NFS.Protocol;
using SecondDimensionWatcherReDive.NFS.Server;
using SecondDimensionWatcherReDive.NFS.Vfs;

namespace SecondDimensionWatcherReDive.NFS;

public static class NfsServiceExtensions
{
    public static IServiceCollection AddNfs(this IServiceCollection services)
    {
        services.AddOptionsWithValidateOnStart<NfsOptions, ValidateNfsOptions>()
            .BindConfiguration(NfsOptions.SectionName);

        services.AddSingleton<NfsClientRegistry>();
        services.AddSingleton<NfsOpenStateRegistry>();
        services.AddScoped<NfsVfsAdapter>();
        services.AddScoped<NfsCompoundDispatcher>();
        services.AddSingleton<NfsTcpServer>();
        services.AddHostedService<NfsBackgroundService>();

        return services;
    }
}
