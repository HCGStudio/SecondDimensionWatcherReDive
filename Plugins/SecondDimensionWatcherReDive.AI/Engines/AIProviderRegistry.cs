using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SecondDimensionWatcherReDive.AI.Abstractions;
using SecondDimensionWatcherReDive.AI.Codex;
using SecondDimensionWatcherReDive.AI.Configuration;
using SecondDimensionWatcherReDive.AI.Models;
using SecondDimensionWatcherReDive.AI.Providers;

namespace SecondDimensionWatcherReDive.AI.Engines;

/// <summary>Builds an isolated engine snapshot for each selected named endpoint.</summary>
public sealed partial class AIProviderRegistry(
    IHttpClientFactory httpClientFactory,
    ICodexAppServerTransportFactory codexTransportFactory,
    ILoggerFactory loggerFactory)
{
    public IAIEngineBackend Create(AIProviderOptions provider)
    {
        if (provider.Protocol == AIProviderProtocol.CodexAppServer)
            return new CodexAppServerEngine(codexTransportFactory,
                new FixedOptionsMonitor<CodexAppServerOptions>(new()
                {
                    Endpoint = provider.Endpoint,
                    BearerToken = provider.BearerToken,
                    Model = provider.Model,
                    PermissionProfile = provider.PermissionProfile,
                    TimeoutSeconds = provider.TimeoutSeconds
                }), loggerFactory.CreateLogger<CodexAppServerEngine>());

        IAIProvider client = provider.Protocol switch
        {
            AIProviderProtocol.OpenAIResponses or AIProviderProtocol.OpenAIChatCompletions =>
                new OpenAIProvider(httpClientFactory, Options.Create(new OpenAIOptions
                {
                    ApiMode = provider.Protocol == AIProviderProtocol.OpenAIResponses
                        ? OpenAIApiMode.Responses : OpenAIApiMode.ChatCompletions,
                    BaseUrl = provider.BaseUrl,
                    ApiKey = provider.ApiKey,
                    AllowAnonymous = AllowsAnonymous(provider),
                    Model = provider.Model,
                    MaxTokens = provider.MaxTokens
                }), loggerFactory.CreateLogger<OpenAIProvider>()),
            AIProviderProtocol.Anthropic =>
                new AnthropicProvider(httpClientFactory, Options.Create(new AnthropicOptions
                {
                    BaseUrl = provider.BaseUrl,
                    ApiKey = provider.ApiKey,
                    Model = provider.Model,
                    MaxTokens = provider.MaxTokens,
                    ApiVersion = provider.ApiVersion
                }), loggerFactory.CreateLogger<AnthropicProvider>()),
            _ => throw new ArgumentException($"Unknown AI protocol '{provider.Protocol}'.")
        };
        return new AIEngine(client, loggerFactory.CreateLogger<AIEngine>());
    }

    public async Task<IReadOnlyList<AIModel>> GetAvailableModelsAsync(
        AIOptions options, CancellationToken cancellationToken)
    {
        var providers = options.Providers.OrderByDescending(entry =>
            string.Equals(entry.Key, options.DefaultProviderId, StringComparison.OrdinalIgnoreCase)).ToArray();
        var results = await Task.WhenAll(providers.Select(async entry =>
        {
            var configured = entry.Value;
            var models = new Dictionary<string, AIModel>(StringComparer.Ordinal);
            var discoveredEfforts = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
            if (IsConfigured(configured))
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(configured.TimeoutSeconds, 1, 15)));
                try
                {
                    var engine = Create(configured);
                    foreach (var model in await engine.GetAvailableModelsAsync(timeout.Token))
                    {
                        models[model.Id] = model;
                        discoveredEfforts[model.Id] = model.ReasoningEfforts;
                    }
                }
                catch (Exception exception) when (!cancellationToken.IsCancellationRequested &&
                    exception is HttpRequestException or OperationCanceledException or InvalidDataException or
                        InvalidOperationException or TimeoutException or System.Text.Json.JsonException or
                        System.Net.WebSockets.WebSocketException)
                {
                    LogDiscoveryFailure(loggerFactory.CreateLogger<AIProviderRegistry>(), entry.Key, exception);
                }
            }

            if (!string.IsNullOrWhiteSpace(configured.Model))
                models.TryAdd(configured.Model, new AIModel(configured.Model, configured.Model, configured.Name));
            foreach (var model in configured.Models.Where(model => !string.IsNullOrWhiteSpace(model.Id)))
                models[model.Id] = new AIModel(model.Id, model.Name ?? model.Id, configured.Name)
                {
                    ReasoningEfforts = GetConfiguredEfforts(model) ??
                        (models.TryGetValue(model.Id, out var discovered) ? discovered.ReasoningEfforts : [])
                };

            return models.Values.Select(model => model with
            {
                ProviderId = entry.Key,
                Provider = string.IsNullOrWhiteSpace(configured.Name) ? entry.Key : configured.Name,
                ReasoningEfforts = configured.Protocol == AIProviderProtocol.CodexAppServer &&
                    discoveredEfforts.TryGetValue(model.Id, out var remoteEfforts) ? remoteEfforts :
                    GetConfiguredEfforts(configured.Models.FirstOrDefault(item => item.Id == model.Id)) ??
                    (model.ReasoningEfforts.Count > 0 ? model.ReasoningEfforts :
                        AIModelCapabilities.GetReasoningEfforts(configured.Protocol, model.Id))
            }).OrderByDescending(model => model.Id == configured.Model).ToList();
        }));
        return results.SelectMany(models => models).ToList();
    }

    public static KeyValuePair<string, AIProviderOptions> Select(AIOptions options, string? providerId)
    {
        var selected = string.IsNullOrWhiteSpace(providerId) ? options.DefaultProviderId : providerId;
        if (string.IsNullOrWhiteSpace(selected)) selected = options.Providers.Keys.FirstOrDefault();
        foreach (var entry in options.Providers)
            if (string.Equals(entry.Key, selected, StringComparison.OrdinalIgnoreCase)) return entry;
        throw new ArgumentException($"AI provider '{selected}' is not configured.");
    }

    public static ChatOptions ResolveSelection(
        string providerId, AIProviderOptions configured, ChatOptions? options, bool requiresTools = false)
    {
        if (options?.MaxTokens is <= 0 || options?.MaxToolRounds < 0)
            throw new ArgumentException("AI token and tool limits are invalid.");
        if (!IsConfigured(configured))
            throw new ArgumentException($"AI provider '{providerId}' is not configured.");
        var model = string.IsNullOrWhiteSpace(options?.Model) ? configured.Model : options.Model;
        var effort = options?.ReasoningEffort ??
            (model == configured.Model ? configured.ReasoningEffort : null);
        var supported = GetConfiguredEfforts(configured.Models.FirstOrDefault(item => item.Id == model)) ??
            AIModelCapabilities.GetReasoningEfforts(configured.Protocol, model);
        // Codex capabilities are authoritative at model/list and validated before turn/start.
        if (configured.Protocol != AIProviderProtocol.CodexAppServer)
            effort = AIModelCapabilities.ValidateEffort(model, effort, supported);
        else if (!string.IsNullOrWhiteSpace(effort))
            effort = effort.Trim().ToLowerInvariant();

        AIModelCapabilities.ValidateToolProtocol(configured.Protocol, configured.BaseUrl, model, effort,
            requiresTools || options?.ToolExecutor?.ToolDefinitions is { Count: > 0 } && options.MaxToolRounds > 0);
        return new ChatOptions
        {
            ProviderId = providerId,
            Model = model,
            ReasoningEffort = effort,
            MaxTokens = options?.MaxTokens ?? configured.MaxTokens,
            MaxToolRounds = options?.MaxToolRounds ?? 8,
            ToolExecutor = options?.ToolExecutor,
            OutputSchema = options?.OutputSchema
        };
    }

    public static bool IsConfigured(AIProviderOptions provider)
        => provider.Protocol == AIProviderProtocol.CodexAppServer
            ? AIEngineStatus.IsCodexConfigured(new CodexAppServerOptions
            {
                Endpoint = provider.Endpoint,
                BearerToken = provider.BearerToken,
                PermissionProfile = provider.PermissionProfile,
                TimeoutSeconds = provider.TimeoutSeconds
            })
            : Enum.IsDefined(provider.Protocol) && AIEngineStatus.IsHttpEndpoint(provider.BaseUrl) &&
              (AllowsAnonymous(provider) || !string.IsNullOrWhiteSpace(provider.ApiKey)) &&
              !string.IsNullOrWhiteSpace(provider.Model) &&
              provider.MaxTokens > 0 && (provider.Protocol != AIProviderProtocol.Anthropic ||
                                        !string.IsNullOrWhiteSpace(provider.ApiVersion));

    private static IReadOnlyList<string>? GetConfiguredEfforts(AIProviderModelOptions? model)
        => model?.ReasoningEffortsConfigured == true ? model.ReasoningEfforts ?? [] : model?.ReasoningEfforts;

    private static bool AllowsAnonymous(AIProviderOptions provider)
        => provider.Protocol is AIProviderProtocol.OpenAIResponses or AIProviderProtocol.OpenAIChatCompletions &&
           !AIModelCapabilities.IsOfficialOpenAIEndpoint(provider.BaseUrl);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Could not discover models for AI provider {ProviderId}; using its configured models.")]
    private static partial void LogDiscoveryFailure(ILogger logger, string providerId, Exception exception);

    private sealed class FixedOptionsMonitor<T>(T value) : IOptionsMonitor<T>
    {
        public T CurrentValue => value;
        public T Get(string? name) => value;
        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }
}
