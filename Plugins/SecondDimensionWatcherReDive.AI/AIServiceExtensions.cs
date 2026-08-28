using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SecondDimensionWatcherReDive.AI.Abstractions;
using SecondDimensionWatcherReDive.AI.Codex;
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
        // Register every backend. The router and built-in engine select from the current
        // IOptionsMonitor snapshot, so a settings update applies to the next request without a
        // process restart. Validation is deliberately performed by the selected backend instead
        // of at startup, because unselected providers are allowed to be unconfigured.
        services.AddOptions<AIOptions>()
            .Bind(configuration.GetSection(AIOptions.SectionName));
        services.AddOptions<OpenAIOptions>()
            .Bind(configuration.GetSection(OpenAIOptions.SectionName));
        services.AddOptions<AnthropicOptions>()
            .Bind(configuration.GetSection(AnthropicOptions.SectionName));
        services.AddOptions<CodexAppServerOptions>()
            .Bind(configuration.GetSection(CodexAppServerOptions.SectionName));

        // Provider credentials must never be replayed by HttpClientHandler to a redirect target.
        // In particular, .NET only strips Authorization automatically; custom headers such as
        // Anthropic's x-api-key would otherwise survive a cross-origin redirect.
        services.AddHttpClient("OpenAI")
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
            {
                AllowAutoRedirect = false
            });
        services.AddHttpClient("AnthropicAI")
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
            {
                AllowAutoRedirect = false
            });

        // Explicit factories choose the reloadable monitor overload. Both providers retain their
        // original IOptions<T> constructors for direct callers and backwards compatibility.
        services.AddScoped<OpenAIProvider>(serviceProvider => new OpenAIProvider(
            serviceProvider.GetRequiredService<IHttpClientFactory>(),
            serviceProvider.GetRequiredService<IOptionsMonitor<OpenAIOptions>>(),
            serviceProvider.GetRequiredService<ILogger<OpenAIProvider>>()));
        services.AddScoped<AnthropicProvider>(serviceProvider => new AnthropicProvider(
            serviceProvider.GetRequiredService<IHttpClientFactory>(),
            serviceProvider.GetRequiredService<IOptionsMonitor<AnthropicOptions>>(),
            serviceProvider.GetRequiredService<ILogger<AnthropicProvider>>()));
        services.AddScoped<IAIProvider>(serviceProvider =>
            serviceProvider.GetRequiredService<OpenAIProvider>());
        services.AddScoped<IAIProvider>(serviceProvider =>
            serviceProvider.GetRequiredService<AnthropicProvider>());

        services.AddSingleton<ICodexAppServerTransportFactory, CodexWebSocketTransportFactory>();
        services.AddScoped(serviceProvider => new AIEngine(
            serviceProvider.GetServices<IAIProvider>(),
            serviceProvider.GetRequiredService<IOptionsMonitor<AIOptions>>(),
            serviceProvider.GetRequiredService<ILogger<AIEngine>>()));
        services.AddScoped<CodexAppServerEngine>();
        services.AddScoped<IAIEngineBackend>(serviceProvider =>
            serviceProvider.GetRequiredService<AIEngine>());
        services.AddScoped<IAIEngineBackend>(serviceProvider =>
            serviceProvider.GetRequiredService<CodexAppServerEngine>());

        services.AddScoped<AIEngineRouter>();
        services.AddScoped<IAIEngine>(serviceProvider =>
            serviceProvider.GetRequiredService<AIEngineRouter>());
        services.AddSingleton<IAIEngineStatus, AIEngineStatus>();

        return services;
    }
}
