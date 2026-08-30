using Microsoft.Extensions.DependencyInjection.Extensions;
using SecondDimensionWatcherReDive.Framework.DataRepository;
using SecondDimensionWatcherReDive.Framework.Plugin;
using SecondDimensionWatcherReDive.Repositories;

namespace SecondDimensionWatcherReDive.PluginPlatform;

internal static class PluginPlatformServiceExtensions
{
    public static IServiceCollection AddPluginPlatform(
        this IServiceCollection services,
        IConfiguration configuration,
        string defaultRootPath)
    {
        services.AddOptions<PluginPlatformOptions>()
            .Bind(configuration.GetSection(PluginPlatformOptions.SectionName))
            .PostConfigure(options =>
            {
                if (string.IsNullOrWhiteSpace(options.RootPath))
                    options.RootPath = Path.GetFullPath(defaultRootPath);
            })
            .Validate(options => !string.IsNullOrWhiteSpace(options.RootPath),
                "RootPath must not be empty.")
            .Validate(options => options.MaximumPackageBytes is >= 1_024 and
                                 <= PluginPlatformOptions.MaximumAllowedPackageBytes,
                "MaximumPackageBytes must be between 1 KiB and 64 MiB.")
            .Validate(options => options.MaximumExpandedBytes >= options.MaximumPackageBytes,
                "MaximumExpandedBytes must be at least MaximumPackageBytes.")
            .Validate(options => options.MaximumExpandedBytes <= 256 * 1024 * 1024,
                "MaximumExpandedBytes must not exceed 256 MiB.")
            .Validate(options => options.MaximumPackageFiles is >= 1 and <= 4_096,
                "MaximumPackageFiles must be between 1 and 4,096.")
            .Validate(options => options.MaximumStagedPackages is >= 1 and <= 1_024,
                "MaximumStagedPackages must be between 1 and 1,024.")
            .Validate(options => options.MaximumStagedPackageBytes >= options.MaximumPackageBytes &&
                                 options.MaximumStagedPackageBytes <= 4L * 1024 * 1024 * 1024,
                "MaximumStagedPackageBytes must be at least MaximumPackageBytes and no greater than 4 GiB.")
            .Validate(options => options.InvocationTimeoutMilliseconds is >= 100 and <= 60_000,
                "Invocation timeout must be between 100 ms and 60 seconds.")
            .Validate(options => options.MaximumWorkerMemoryMegabytes is >= 32 and <= 1_024,
                "Worker memory must be between 32 MiB and 1 GiB.")
            .Validate(options => options.MaximumWorkerCpuMilliseconds is >= 100 and <= 60_000,
                "Worker CPU time must be between 100 ms and 60 seconds.")
            .Validate(options => options.MaximumConcurrentWorkers is >= 1 and <= 32,
                "MaximumConcurrentWorkers must be between 1 and 32.")
            .Validate(options => options.MaximumConcurrentWorkersPerPlugin >= 1 &&
                                 options.MaximumConcurrentWorkersPerPlugin <= options.MaximumConcurrentWorkers,
                "The per-plugin worker limit must be positive and no greater than the global worker limit.")
            .Validate(options => options.MaximumPluginDataBytes is >= 1_024 and <= 10L * 1024 * 1024 * 1024,
                "MaximumPluginDataBytes must be between 1 KiB and 10 GiB.")
            .Validate(options => options.MaximumPluginDataFiles is >= 1 and <= 100_000,
                "MaximumPluginDataFiles must be between 1 and 100,000.")
            .Validate(options => options.MaximumPluginDataPathDepth is >= 1 and <= 64,
                "MaximumPluginDataPathDepth must be between 1 and 64.")
            .Validate(options => options.MaximumResponseBytes is >= 1_024 and <= 8 * 1024 * 1024,
                "MaximumResponseBytes must be between 1 KiB and 8 MiB.")
            .Validate(options => options.CircuitBreakerFailures is >= 1 and <= 100,
                "CircuitBreakerFailures must be between 1 and 100.")
            .Validate(options => options.CircuitBreakerSeconds is >= 1 and <= 86_400,
                "CircuitBreakerSeconds must be between 1 second and 1 day.")
            .Validate(options => options.PreviewLifetimeMinutes is >= 1 and <= 1_440,
                "PreviewLifetimeMinutes must be between 1 minute and 1 day.")
            .ValidateOnStart();
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<IPluginCatalogRepository, PluginCatalogRepository>();
        services.AddSingleton<PluginPackageInspector>();
        services.AddSingleton<PluginSafeFileAccess>();
        services.AddSingleton<IPluginCapabilityBroker, PluginCapabilityBroker>();
        services.AddSingleton<IPluginDnsResolver, SystemPluginDnsResolver>();
        services.AddSingleton<IPluginProcessExecutor, PluginProcessExecutor>();
        services.AddSingleton<PluginManager>();
        services.AddSingleton<IPluginManager>(provider => provider.GetRequiredService<PluginManager>());
        services.AddSingleton<IJavaScriptPluginLoader>(provider => provider.GetRequiredService<PluginManager>());
        services.AddSingleton<IPluginProviderRegistry, PluginProviderRegistry>();
        services.AddHttpClient("PluginPlatform")
            .ConfigurePrimaryHttpMessageHandler(provider =>
                PluginNetworkConnectionFactory.Create(provider.GetRequiredService<IPluginDnsResolver>()));
        return services;
    }
}
