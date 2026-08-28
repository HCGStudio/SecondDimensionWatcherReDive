using Microsoft.Extensions.Options;
using SecondDimensionWatcherReDive.AI.Abstractions;
using SecondDimensionWatcherReDive.AI.Configuration;

namespace SecondDimensionWatcherReDive.AI.Engines;

/// <summary>
///     Options-only status view. It is safe for singleton hosted services to consume and reflects
///     configuration reloads without resolving the scoped engine graph.
/// </summary>
public sealed class AIEngineStatus(
    IOptionsMonitor<AIOptions> aiOptions,
    IOptionsMonitor<OpenAIOptions> openAIOptions,
    IOptionsMonitor<AnthropicOptions> anthropicOptions,
    IOptionsMonitor<CodexAppServerOptions> codexOptions) : IAIEngineStatus
{
    public string Name => aiOptions.CurrentValue.Engine switch
    {
        AIEngineKind.BuiltIn => $"BuiltIn/{aiOptions.CurrentValue.Provider}",
        AIEngineKind.CodexAppServer => "CodexAppServer",
        var kind => kind.ToString()
    };

    public bool IsConfigured => aiOptions.CurrentValue.Engine switch
    {
        AIEngineKind.BuiltIn => IsBuiltInConfigured(aiOptions.CurrentValue.Provider),
        AIEngineKind.CodexAppServer => IsCodexConfigured(codexOptions.CurrentValue),
        _ => false
    };

    private bool IsBuiltInConfigured(string provider)
        => provider switch
        {
            var value when string.Equals(value, "OpenAI", StringComparison.OrdinalIgnoreCase) =>
                IsOpenAIConfigured(openAIOptions.CurrentValue),
            var value when string.Equals(value, "Anthropic", StringComparison.OrdinalIgnoreCase) =>
                IsAnthropicConfigured(anthropicOptions.CurrentValue),
            _ => false
        };

    private static bool IsOpenAIConfigured(OpenAIOptions options)
        => IsHttpEndpoint(options.BaseUrl)
           && !string.IsNullOrWhiteSpace(options.ApiKey)
           && !string.IsNullOrWhiteSpace(options.Model)
           && options.MaxTokens > 0;

    private static bool IsAnthropicConfigured(AnthropicOptions options)
        => IsHttpEndpoint(options.BaseUrl)
           && !string.IsNullOrWhiteSpace(options.ApiKey)
           && !string.IsNullOrWhiteSpace(options.Model)
           && !string.IsNullOrWhiteSpace(options.ApiVersion)
           && options.MaxTokens > 0;

    private static bool IsCodexConfigured(CodexAppServerOptions options)
        => options.TimeoutSeconds > 0
           && !string.IsNullOrWhiteSpace(options.PermissionProfile)
           && Uri.TryCreate(options.Endpoint, UriKind.Absolute, out var endpoint)
           && endpoint.Scheme is "ws" or "wss"
           && string.IsNullOrEmpty(endpoint.UserInfo)
           && string.IsNullOrEmpty(endpoint.Query)
           && string.IsNullOrEmpty(endpoint.Fragment)
           && (endpoint.Scheme == "wss" || endpoint.IsLoopback)
           && (endpoint.IsLoopback || !string.IsNullOrWhiteSpace(options.BearerToken));

    private static bool IsHttpEndpoint(string value)
        => Uri.TryCreate(value, UriKind.Absolute, out var endpoint)
           && endpoint.Scheme is "http" or "https"
           && string.IsNullOrEmpty(endpoint.UserInfo)
           && string.IsNullOrEmpty(endpoint.Query)
           && string.IsNullOrEmpty(endpoint.Fragment);
}
