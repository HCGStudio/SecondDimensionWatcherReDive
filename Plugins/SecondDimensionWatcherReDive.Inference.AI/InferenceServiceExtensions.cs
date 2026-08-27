using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SecondDimensionWatcherReDive.AI;
using SecondDimensionWatcherReDive.Framework.Inference;
using SecondDimensionWatcherReDive.Inference.AI.Configuration;
using SecondDimensionWatcherReDive.Inference.AI.Engines;
using SecondDimensionWatcherReDive.Inference.AI.Tools;
using TMDbLib.Client;

namespace SecondDimensionWatcherReDive.Inference.AI;

public static class InferenceServiceExtensions
{
    public static IServiceCollection AddTmdbMetadata(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var tmdbApiKey = configuration["TmdbApiKey"];
        var isConfigured = !string.IsNullOrWhiteSpace(tmdbApiKey);
        services.TryAddSingleton(_ => new TMDbClient(
            isConfigured ? tmdbApiKey! : "tmdb-api-key-not-configured"));
        services.TryAddSingleton(serviceProvider => new TmdbTool(
            serviceProvider.GetRequiredService<TMDbClient>(),
            serviceProvider.GetRequiredService<Microsoft.Extensions.Logging.ILogger<TmdbTool>>(),
            isConfigured));
        return services;
    }

    public static IServiceCollection AddAIInference(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddTmdbMetadata(configuration);

        // Register AI engine (provider selection handled inside)
        services.AddAIEngine(configuration);

        // Register inference-specific options
        services.AddOptionsWithValidateOnStart<InferenceOptions, ValidateInferenceOptions>()
            .BindConfiguration(InferenceOptions.SectionName);

        // Register TMDB tool and individual tool classes
        services.AddSingleton<SearchTmdbTool>();
        services.AddSingleton<GetTmdbSeasonsTool>();
        services.AddSingleton<GetTmdbSeasonEpisodesTool>();
        services.AddScoped<FileNameInferenceContext>();
        services.AddScoped<SaveFileNameRegexRuleTool>();

        // Register the inference engine (single implementation, provider-agnostic)
        services.AddScoped<IInferenceEngine, InferenceEngine>();

        return services;
    }
}
