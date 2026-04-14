using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SecondDimensionWatcherReDive.AI;
using SecondDimensionWatcherReDive.Framework.Inference;
using SecondDimensionWatcherReDive.Inference.AI.Configuration;
using SecondDimensionWatcherReDive.Inference.AI.Engines;
using SecondDimensionWatcherReDive.Inference.AI.Tools;
using TMDbLib.Client;

namespace SecondDimensionWatcherReDive.Inference.AI;

public static class InferenceServiceExtensions
{
    public static IServiceCollection AddAIInference(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Register AI engine (provider selection handled inside)
        services.AddAiEngine(configuration);

        // Register inference-specific options
        services.AddOptionsWithValidateOnStart<InferenceOptions, ValidateInferenceOptions>()
            .BindConfiguration(InferenceOptions.SectionName);

        // Register TMDB tool
        var tmdbApiKey = configuration["TmdbApiKey"];
        services.AddSingleton(_ => new TMDbClient(tmdbApiKey ?? string.Empty));
        services.AddSingleton<TmdbTool>();

        // Register the inference engine (single implementation, provider-agnostic)
        services.AddScoped<IInferenceEngine, InferenceEngine>();

        return services;
    }
}
