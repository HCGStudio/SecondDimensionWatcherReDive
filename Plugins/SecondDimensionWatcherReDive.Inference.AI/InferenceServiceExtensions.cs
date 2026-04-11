using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
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
        services.Configure<InferenceOptions>(configuration.GetSection(InferenceOptions.SectionName));

        var inferenceOptions = configuration.GetSection(InferenceOptions.SectionName).Get<InferenceOptions>();

        services.AddHttpClient("InferenceEngine");

        // Register TMDB tool
        var tmdbApiKey = configuration["TmdbApiKey"];
        services.AddSingleton(_ => new TMDbClient(tmdbApiKey ?? string.Empty));
        services.AddSingleton<TmdbTool>();

        // Register the correct engine based on Provider config
        if (string.Equals(inferenceOptions?.Provider, "Anthropic", StringComparison.OrdinalIgnoreCase))
        {
            services.AddScoped<IInferenceEngine, AnthropicCompatibleEngine>();
        }
        else
        {
            services.AddScoped<IInferenceEngine, OpenAiCompatibleEngine>();
        }

        return services;
    }
}
