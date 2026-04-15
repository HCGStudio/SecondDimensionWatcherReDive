using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SecondDimensionWatcherReDive.AI.Abstractions;
using SecondDimensionWatcherReDive.AI.Configuration;
using SecondDimensionWatcherReDive.AI.Engines;
using SecondDimensionWatcherReDive.AI.Providers;

namespace SecondDimensionWatcherReDive.AI;

public static class AIServiceExtensions
{
    public static IServiceCollection AddAIEngine(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var provider = configuration["AI:Provider"]
            is { Length: > 0 } p
            ? p
            : "OpenAI";

        if (string.Equals(provider, "Anthropic", StringComparison.OrdinalIgnoreCase))
        {
            services.AddOptionsWithValidateOnStart<AnthropicOptions, ValidateAnthropicOptions>()
                .BindConfiguration(AnthropicOptions.SectionName);

            services.AddHttpClient("AnthropicAI", (sp, client) =>
            {
                var opts = sp.GetRequiredService<IOptions<AnthropicOptions>>().Value;
                client.BaseAddress = new(opts.BaseUrl.TrimEnd('/') + "/");
                client.DefaultRequestHeaders.Add("x-api-key", opts.ApiKey);
                client.DefaultRequestHeaders.Add("anthropic-version", opts.ApiVersion);
            });

            services.AddScoped<IAIProvider, AnthropicProvider>();
        }
        else
        {
            services.AddOptionsWithValidateOnStart<OpenAIOptions, ValidateOpenAIOptions>()
                .BindConfiguration(OpenAIOptions.SectionName);

            services.AddHttpClient("OpenAI", (sp, client) =>
            {
                var opts = sp.GetRequiredService<IOptions<OpenAIOptions>>().Value;
                client.BaseAddress = new(opts.BaseUrl.TrimEnd('/') + "/");
                client.DefaultRequestHeaders.Authorization =
                    new("Bearer", opts.ApiKey);
            });

            services.AddScoped<IAIProvider, OpenAIProvider>();
        }

        services.AddScoped<IAIEngine, AIEngine>();

        return services;
    }
}
