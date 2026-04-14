using System.Net.Http.Headers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SecondDimensionWatcherReDive.AI.Abstractions;
using SecondDimensionWatcherReDive.AI.Configuration;
using SecondDimensionWatcherReDive.AI.Engines;

namespace SecondDimensionWatcherReDive.AI;

public static class AiServiceExtensions
{
    public static IServiceCollection AddAiEngine(
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

            services.AddHttpClient("AnthropicAi", (sp, client) =>
            {
                var opts = sp.GetRequiredService<IOptions<AnthropicOptions>>().Value;
                client.BaseAddress = new Uri(opts.BaseUrl.TrimEnd('/') + "/");
                client.DefaultRequestHeaders.Add("x-api-key", opts.ApiKey);
                client.DefaultRequestHeaders.Add("anthropic-version", opts.ApiVersion);
            });

            services.AddScoped<IAiEngine, AnthropicCompatibleEngine>();
        }
        else
        {
            services.AddOptionsWithValidateOnStart<OpenAiOptions, ValidateOpenAiOptions>()
                .BindConfiguration(OpenAiOptions.SectionName);

            services.AddHttpClient("OpenAi", (sp, client) =>
            {
                var opts = sp.GetRequiredService<IOptions<OpenAiOptions>>().Value;
                client.BaseAddress = new Uri(opts.BaseUrl.TrimEnd('/') + "/");
                client.DefaultRequestHeaders.Authorization =
                    new AuthenticationHeaderValue("Bearer", opts.ApiKey);
            });

            services.AddScoped<IAiEngine, OpenAiCompatibleEngine>();
        }

        return services;
    }
}
