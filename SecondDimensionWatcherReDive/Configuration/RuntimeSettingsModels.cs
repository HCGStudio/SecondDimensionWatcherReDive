using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;
using SecondDimensionWatcherReDive.AI.Models;
using SecondDimensionWatcherReDive.Framework.Notifications;
using SecondDimensionWatcherReDive.Framework.Networking;

namespace SecondDimensionWatcherReDive.Configuration;

[JsonConverter(typeof(JsonStringEnumConverter<AiExecutionMode>))]
internal enum AiExecutionMode
{
    [JsonStringEnumMemberName("builtIn")]
    BuiltIn,

    [JsonStringEnumMemberName("codexAppServer")]
    CodexAppServer
}

[JsonConverter(typeof(JsonStringEnumConverter<BuiltInAiProvider>))]
internal enum BuiltInAiProvider
{
    [JsonStringEnumMemberName("openAI")]
    OpenAI,

    [JsonStringEnumMemberName("anthropic")]
    Anthropic
}

[JsonConverter(typeof(JsonStringEnumConverter<OpenAiApiMode>))]
internal enum OpenAiApiMode
{
    [JsonStringEnumMemberName("responses")]
    Responses,

    [JsonStringEnumMemberName("chatCompletions")]
    ChatCompletions
}

[JsonConverter(typeof(JsonStringEnumConverter<AiProviderProtocol>))]
internal enum AiProviderProtocol
{
    [JsonStringEnumMemberName("openAIResponses")]
    OpenAIResponses,
    [JsonStringEnumMemberName("openAIChatCompletions")]
    OpenAIChatCompletions,
    [JsonStringEnumMemberName("anthropic")]
    Anthropic,
    [JsonStringEnumMemberName("codexAppServer")]
    CodexAppServer
}

internal sealed record AiModelSettingsValues(
    string Id,
    string? Name,
    IReadOnlyList<string> ReasoningEfforts);

internal sealed record AiProviderSettingsValues
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public AiProviderProtocol Protocol { get; init; }
    public string BaseUrl { get; init; } = "https://api.openai.com/v1";
    public string Model { get; init; } = "gpt-5.6-luna";
    public int MaxTokens { get; init; } = 16384;
    public string? ReasoningEffort { get; init; }
    public string ApiVersion { get; init; } = "2023-06-01";
    public string Endpoint { get; init; } = string.Empty;
    public string PermissionProfile { get; init; } = ":read-only";
    public int TimeoutSeconds { get; init; } = 300;
    public IReadOnlyList<AiModelSettingsValues> Models { get; init; } = [];
}

[JsonConverter(typeof(JsonStringEnumConverter<SecretMutationOperation>))]
internal enum SecretMutationOperation
{
    [JsonStringEnumMemberName("keep")]
    Keep,

    [JsonStringEnumMemberName("set")]
    Set,

    [JsonStringEnumMemberName("clear")]
    Clear,

    [JsonStringEnumMemberName("reset")]
    Reset
}

[JsonConverter(typeof(JsonStringEnumConverter<SecretConfigurationSource>))]
internal enum SecretConfigurationSource
{
    [JsonStringEnumMemberName("runtime")]
    Runtime,

    [JsonStringEnumMemberName("deployment")]
    Deployment,

    [JsonStringEnumMemberName("none")]
    None
}

[JsonConverter(typeof(JsonStringEnumConverter<PersistedSecretMode>))]
internal enum PersistedSecretMode
{
    [JsonStringEnumMemberName("set")]
    Set,

    [JsonStringEnumMemberName("clear")]
    Clear
}

internal sealed record OpenAiSettingsValues(
    string BaseUrl,
    OpenAiApiMode ApiMode,
    string Model,
    int MaxTokens);

internal sealed record AnthropicSettingsValues(
    string BaseUrl,
    string Model,
    int MaxTokens,
    string ApiVersion);

internal sealed record CodexAppServerSettingsValues(
    string Endpoint,
    string? Model,
    string PermissionProfile,
    int TimeoutSeconds);

internal sealed record InferenceSettingsValues(int RateLimitDelayMs)
{
    public string? ProviderId { get; init; }
    public string? Model { get; init; }
    public string? ReasoningEffort { get; init; }
}

internal sealed record AiSettingsValues(
    AiExecutionMode ExecutionMode,
    BuiltInAiProvider Provider,
    OpenAiSettingsValues OpenAI,
    AnthropicSettingsValues Anthropic,
    CodexAppServerSettingsValues CodexAppServer,
    InferenceSettingsValues Inference)
{
    // Null represents the legacy single-provider configuration; an empty list is explicit.
    public IReadOnlyList<AiProviderSettingsValues>? Providers { get; init; }
    public string? DefaultProviderId { get; init; }
}

internal sealed record TorrentSettingsValues(
    string Url,
    string? UserName,
    string? UserAgent);

internal sealed record MediaLibrarySettingsValues(
    IReadOnlyList<string> AllowedRoots,
    TimeSpan ScanInterval,
    TimeSpan SettlingPeriod,
    TimeSpan MissingGracePeriod);

internal sealed record IncidentDiskSettingsValues(
    long MinimumAvailableBytes,
    double MinimumAvailablePercent);

internal sealed record IncidentSettingsValues(
    TimeSpan DownloadStalledAfter,
    TimeSpan ReportThrottle,
    TimeSpan ReconciliationInterval,
    IncidentDiskSettingsValues Disk);

internal sealed record NfsSettingsValues(
    bool Enabled,
    int Port,
    string BindAddress,
    int LeaseSeconds,
    int MaxConnections)
{
    public int IdleTimeoutSeconds { get; init; } = 120;

    public bool AllowAnonymous { get; init; }

    public IReadOnlyList<string> AllowedNetworks { get; init; } = ["127.0.0.0/8", "::1/128"];
}

internal sealed record NotificationSettingsValues(
    bool WebhookEnabled,
    bool WebPushEnabled,
    string WebPushSubject,
    string VapidPublicKey,
    IReadOnlyList<NotificationEventType> Events,
    TimeSpan? QuietHoursStart,
    TimeSpan? QuietHoursEnd,
    string TimeZoneId);

internal sealed record RuntimeSettingsValues(
    AiSettingsValues Ai,
    TorrentSettingsValues Torrent,
    MediaLibrarySettingsValues MediaLibrary,
    IncidentSettingsValues Incidents,
    NfsSettingsValues Nfs,
    NotificationSettingsValues Notifications);

internal sealed record RuntimeSettingsOverrides
{
    public AiSettingsValues? Ai { get; init; }

    public TorrentSettingsValues? Torrent { get; init; }

    public MediaLibrarySettingsValues? MediaLibrary { get; init; }

    public IncidentSettingsValues? Incidents { get; init; }

    public NfsSettingsValues? Nfs { get; init; }

    public NotificationSettingsValues? Notifications { get; init; }
}

internal sealed record PersistedSecret(PersistedSecretMode Mode, string? Value);

internal sealed record RuntimeSecretOverrides
{
    public Dictionary<string, PersistedSecret> Values { get; init; } =
        new(StringComparer.OrdinalIgnoreCase);
}

internal sealed record SecretMutation(
    SecretMutationOperation Operation,
    string? Value);

internal sealed record AiSettingsUpdate(
    AiSettingsValues Values,
    SecretMutation? OpenAiApiKey,
    SecretMutation? AnthropicApiKey,
    SecretMutation? CodexToken)
{
    public IReadOnlyDictionary<string, SecretMutation?> ProviderApiKeys { get; init; } =
        new Dictionary<string, SecretMutation?>();
    public IReadOnlyDictionary<string, SecretMutation?> ProviderTokens { get; init; } =
        new Dictionary<string, SecretMutation?>();
}

internal sealed record TmdbSettingsUpdate(SecretMutation? ApiKey);

internal sealed record TorrentSettingsUpdate(
    TorrentSettingsValues Values,
    SecretMutation? Password);

internal sealed record NotificationSettingsUpdate(
    bool WebhookEnabled,
    bool WebPushEnabled,
    string WebPushSubject,
    IReadOnlyList<NotificationEventType> Events,
    TimeSpan? QuietHoursStart,
    TimeSpan? QuietHoursEnd,
    string TimeZoneId,
    SecretMutation? WebhookUrl,
    bool GenerateVapidKeys);

internal sealed record RuntimeSettingsPatch(
    long ExpectedRevision,
    AiSettingsUpdate? Ai,
    TmdbSettingsUpdate? Tmdb,
    TorrentSettingsUpdate? Torrent,
    MediaLibrarySettingsValues? MediaLibrary,
    IncidentSettingsValues? Incidents,
    NfsSettingsValues? Nfs,
    NotificationSettingsUpdate? Notifications = null);

internal sealed record ResolvedSecret(
    string? Value,
    bool IsConfigured,
    SecretConfigurationSource Source);

internal sealed record RuntimeSettingsState(
    long Revision,
    RuntimeSettingsValues Desired,
    IReadOnlyDictionary<string, ResolvedSecret> Secrets,
    bool PendingRestart);

internal enum RuntimeSettingsUpdateStatus
{
    Saved,
    Conflict,
    Invalid
}

internal sealed record RuntimeSettingsUpdateResult(
    RuntimeSettingsUpdateStatus Status,
    RuntimeSettingsState State,
    IReadOnlyDictionary<string, string[]> Errors);

internal static class RuntimeSecretKeys
{
    public const string OpenAiApiKey = "AI:OpenAI:ApiKey";
    public const string AnthropicApiKey = "AI:Anthropic:ApiKey";
    public const string CodexToken = "AI:CodexAppServer:BearerToken";
    public const string TmdbApiKey = "TmdbApiKey";
    public const string TorrentPassword = "Torrent:Remote:Password";
    public const string NotificationWebhookUrl = "Notifications:Webhook:Url";
    public const string NotificationVapidPrivateKey = "Notifications:WebPush:VapidPrivateKey";

    public static string ProviderApiKey(string id) => $"AI:Providers:{id}:ApiKey";
    public static string ProviderToken(string id) => $"AI:Providers:{id}:BearerToken";

    public static bool IsProviderSecret(string key) =>
        Regex.IsMatch(key, @"^AI:Providers:[a-zA-Z0-9][a-zA-Z0-9_-]{0,63}:(ApiKey|BearerToken)$",
            RegexOptions.CultureInvariant);

    public static IEnumerable<string> ForProviders(AiSettingsValues ai) =>
        RuntimeAiProviders.GetProviders(ai).SelectMany(provider => new[]
        {
            ProviderApiKey(provider.Id), ProviderToken(provider.Id)
        });

    public static readonly string[] All =
    [
        OpenAiApiKey,
        AnthropicApiKey,
        CodexToken,
        TmdbApiKey,
        TorrentPassword,
        NotificationWebhookUrl,
        NotificationVapidPrivateKey
    ];
}

internal static class RuntimeAiProviders
{
    public static IReadOnlyList<AiProviderSettingsValues> GetProviders(AiSettingsValues ai)
    {
        if (ai.Providers is not null)
            return ai.Providers;
        var providers = new List<AiProviderSettingsValues>
        {
            new()
            {
                Id = "openai", Name = "OpenAI",
                Protocol = ai.OpenAI.ApiMode == OpenAiApiMode.ChatCompletions
                    ? AiProviderProtocol.OpenAIChatCompletions : AiProviderProtocol.OpenAIResponses,
                BaseUrl = ai.OpenAI.BaseUrl, Model = ai.OpenAI.Model, MaxTokens = ai.OpenAI.MaxTokens
            },
            new()
            {
                Id = "anthropic", Name = "Anthropic", Protocol = AiProviderProtocol.Anthropic,
                BaseUrl = ai.Anthropic.BaseUrl, Model = ai.Anthropic.Model,
                MaxTokens = ai.Anthropic.MaxTokens, ApiVersion = ai.Anthropic.ApiVersion
            }
        };
        if (ai.ExecutionMode == AiExecutionMode.CodexAppServer
            || !string.IsNullOrWhiteSpace(ai.CodexAppServer.Endpoint))
            providers.Add(new AiProviderSettingsValues
            {
                Id = "codex", Name = "Codex", Protocol = AiProviderProtocol.CodexAppServer,
                Model = ai.CodexAppServer.Model ?? string.Empty,
                Endpoint = ai.CodexAppServer.Endpoint,
                PermissionProfile = ai.CodexAppServer.PermissionProfile,
                TimeoutSeconds = ai.CodexAppServer.TimeoutSeconds
            });
        return providers;
    }

    public static string? DefaultProviderId(AiSettingsValues ai) => ai.Providers is not null
        ? ai.DefaultProviderId ?? ai.Providers.FirstOrDefault()?.Id
        : ai.ExecutionMode == AiExecutionMode.CodexAppServer ? "codex"
            : ai.Provider == BuiltInAiProvider.Anthropic ? "anthropic" : "openai";

    public static string? LegacySecret(string key) => key switch
    {
        "AI:Providers:openai:ApiKey" => RuntimeSecretKeys.OpenAiApiKey,
        "AI:Providers:anthropic:ApiKey" => RuntimeSecretKeys.AnthropicApiKey,
        "AI:Providers:codex:BearerToken" => RuntimeSecretKeys.CodexToken,
        _ => null
    };
}

internal static class RuntimeSettingsDefaults
{
    public static RuntimeSettingsValues FromConfiguration(IConfiguration configuration) =>
        new(
            ReadAi(configuration),
            new TorrentSettingsValues(
                configuration["Torrent:Remote:Url"] ?? "http://localhost:8080",
                NullIfWhiteSpace(configuration["Torrent:Remote:UserName"]),
                NullIfWhiteSpace(configuration["Torrent:Remote:UserAgent"])),
            new MediaLibrarySettingsValues(
                configuration.GetSection("MediaLibrary:AllowedRoots").Get<string[]>() ?? [],
                ReadTimeSpan(configuration, "MediaLibrary:ScanInterval", TimeSpan.FromMinutes(5)),
                ReadTimeSpan(configuration, "MediaLibrary:SettlingPeriod", TimeSpan.FromSeconds(30)),
                ReadTimeSpan(configuration, "MediaLibrary:MissingGracePeriod", TimeSpan.FromHours(24))),
            new IncidentSettingsValues(
                ReadTimeSpan(configuration, "Incidents:DownloadStalledAfter", TimeSpan.FromMinutes(15)),
                ReadTimeSpan(configuration, "Incidents:ReportThrottle", TimeSpan.FromMinutes(5)),
                ReadTimeSpan(configuration, "Incidents:ReconciliationInterval", TimeSpan.FromMinutes(5)),
                new IncidentDiskSettingsValues(
                    configuration.GetValue<long?>("Incidents:Disk:MinimumAvailableBytes")
                    ?? 5L * 1024 * 1024 * 1024,
                    configuration.GetValue<double?>("Incidents:Disk:MinimumAvailablePercent") ?? 5)),
            new NfsSettingsValues(
                configuration.GetValue<bool?>("Nfs:Enabled") ?? false,
                configuration.GetValue<int?>("Nfs:Port") ?? 2049,
                configuration["Nfs:BindAddress"] ?? "127.0.0.1",
                configuration.GetValue<int?>("Nfs:LeaseSeconds") ?? 90,
                configuration.GetValue<int?>("Nfs:MaxConnections") ?? 32)
            {
                IdleTimeoutSeconds = configuration.GetValue<int?>("Nfs:IdleTimeoutSeconds") ?? 120,
                AllowAnonymous = configuration.GetValue<bool?>("Nfs:AllowAnonymous") ?? false,
                AllowedNetworks = configuration.GetSection("Nfs:AllowedNetworks").Get<string[]>()
                                  ?? ["127.0.0.0/8", "::1/128"]
            },
            new NotificationSettingsValues(
                configuration.GetValue<bool?>("Notifications:Webhook:Enabled") ?? false,
                configuration.GetValue<bool?>("Notifications:WebPush:Enabled") ?? false,
                configuration["Notifications:WebPush:Subject"] ?? string.Empty,
                configuration["Notifications:WebPush:VapidPublicKey"] ?? string.Empty,
                ReadNotificationEvents(configuration["Notifications:Events"]),
                configuration.GetValue<TimeSpan?>("Notifications:QuietHours:Start"),
                configuration.GetValue<TimeSpan?>("Notifications:QuietHours:End"),
                configuration["Notifications:QuietHours:TimeZone"] ?? "UTC"));

    public static IReadOnlyDictionary<string, string?> ReadDeploymentSecrets(IConfiguration configuration)
    {
        var ai = ReadAi(configuration);
        var secrets = RuntimeSecretKeys.All.Concat(RuntimeSecretKeys.ForProviders(ai))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToDictionary(key => key, key => NullIfWhiteSpace(configuration[key]), StringComparer.OrdinalIgnoreCase);
        if (ai.Providers is null)
            foreach (var key in RuntimeSecretKeys.ForProviders(ai))
                if (RuntimeAiProviders.LegacySecret(key) is { } legacyKey)
                    secrets[key] = secrets[legacyKey];
        return secrets;
    }

    private static AiSettingsValues ReadAi(IConfiguration configuration) =>
        new(
            ParseEnum(configuration["AI:Engine"], AiExecutionMode.BuiltIn),
            ParseEnum(configuration["AI:Provider"], BuiltInAiProvider.OpenAI),
            new OpenAiSettingsValues(
                configuration["AI:OpenAI:BaseUrl"] ?? "https://api.openai.com/v1",
                ParseEnum(configuration["AI:OpenAI:ApiMode"], OpenAiApiMode.Responses),
                configuration["AI:OpenAI:Model"] ?? "gpt-5.6-luna",
                configuration.GetValue<int?>("AI:OpenAI:MaxTokens") ?? 16384),
            new AnthropicSettingsValues(
                configuration["AI:Anthropic:BaseUrl"] ?? "https://api.anthropic.com",
                configuration["AI:Anthropic:Model"] ?? "claude-sonnet-5",
                configuration.GetValue<int?>("AI:Anthropic:MaxTokens") ?? 16384,
                configuration["AI:Anthropic:ApiVersion"] ?? "2023-06-01"),
            new CodexAppServerSettingsValues(
                configuration["AI:CodexAppServer:Endpoint"] ?? string.Empty,
                NullIfWhiteSpace(configuration["AI:CodexAppServer:Model"]),
                configuration["AI:CodexAppServer:PermissionProfile"] ?? ":read-only",
                configuration.GetValue<int?>("AI:CodexAppServer:TimeoutSeconds") ?? 300),
            new InferenceSettingsValues(
                configuration.GetValue<int?>("Inference:RateLimitDelayMs") ?? 1000)
            {
                ProviderId = NullIfWhiteSpace(configuration["Inference:ProviderId"]),
                Model = NullIfWhiteSpace(configuration["Inference:Model"]),
                ReasoningEffort = NullIfWhiteSpace(configuration["Inference:ReasoningEffort"])
            })
        {
            DefaultProviderId = NullIfWhiteSpace(configuration["AI:DefaultProviderId"]),
            Providers = ReadProviders(configuration)
        };

    private static IReadOnlyList<AiProviderSettingsValues>? ReadProviders(IConfiguration configuration)
    {
        var children = configuration.GetSection("AI:Providers").GetChildren().ToArray();
        if (children.Length == 0 && configuration.GetValue<bool?>("AI:ProvidersConfigured") != true)
            return null;
        return children.Select(section => new AiProviderSettingsValues
        {
            Id = section.Key,
            Name = section["Name"] ?? section.Key,
            Protocol = ParseEnum(section["Protocol"], AiProviderProtocol.OpenAIResponses),
            BaseUrl = section["BaseUrl"] ?? (string.Equals(section["Protocol"], "Anthropic", StringComparison.OrdinalIgnoreCase)
                ? "https://api.anthropic.com" : "https://api.openai.com/v1"),
            Model = section["Model"] ?? (string.Equals(section["Protocol"], "Anthropic", StringComparison.OrdinalIgnoreCase)
                ? "claude-sonnet-5" : string.Equals(section["Protocol"], "CodexAppServer", StringComparison.OrdinalIgnoreCase)
                    ? string.Empty : "gpt-5.6-luna"),
            MaxTokens = section.GetValue<int?>("MaxTokens") ?? 16384,
            ReasoningEffort = NullIfWhiteSpace(section["ReasoningEffort"]),
            ApiVersion = section["ApiVersion"] ?? "2023-06-01",
            Endpoint = section["Endpoint"] ?? string.Empty,
            PermissionProfile = section["PermissionProfile"] ?? ":read-only",
            TimeoutSeconds = section.GetValue<int?>("TimeoutSeconds") ?? 300,
            Models = section.GetSection("Models").GetChildren().Select(model =>
                new AiModelSettingsValues(model["Id"] ?? string.Empty,
                    NullIfWhiteSpace(model["Name"]),
                    model.GetSection("ReasoningEfforts").Get<string[]>()
                    ?? (model.GetValue<bool?>("ReasoningEffortsConfigured") == true ? []
                        : AIModelCapabilities.GetReasoningEfforts(
                            ParseEnum(section["Protocol"], AI.Configuration.AIProviderProtocol.OpenAIResponses),
                            model["Id"] ?? string.Empty)))).ToArray()
        }).ToArray();
    }

    private static TimeSpan ReadTimeSpan(
        IConfiguration configuration,
        string key,
        TimeSpan fallback) =>
        configuration.GetValue<TimeSpan?>(key) ?? fallback;

    private static IReadOnlyList<NotificationEventType> ReadNotificationEvents(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return Enum.GetValues<NotificationEventType>()
                .Where(type => type != NotificationEventType.Test)
                .ToArray();

        return value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(item => Enum.TryParse<NotificationEventType>(item, true, out var parsed)
                ? parsed
                : (NotificationEventType)(-1))
            .ToArray();
    }

    private static TEnum ParseEnum<TEnum>(string? value, TEnum fallback)
        where TEnum : struct, Enum
    {
        if (value is null) return fallback;

        return Enum.TryParse<TEnum>(value, ignoreCase: true, out var parsed)
            ? parsed
            : (TEnum)Enum.ToObject(typeof(TEnum), -1);
    }

    private static string? NullIfWhiteSpace(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;
}

internal static class RuntimeSettingsValidator
{
    public static IReadOnlyDictionary<string, string[]> Validate(
        RuntimeSettingsValues values,
        IReadOnlyDictionary<string, ResolvedSecret> secrets)
    {
        var errors = new Dictionary<string, List<string>>(StringComparer.Ordinal);

        if (values.Ai.Providers is null)
        {
            if (!Enum.IsDefined(values.Ai.ExecutionMode))
                Add(errors, "ai.executionMode", "The AI execution mode is invalid.");
            if (!Enum.IsDefined(values.Ai.Provider))
                Add(errors, "ai.provider", "The built-in AI provider is invalid.");
            if (!Enum.IsDefined(values.Ai.OpenAI.ApiMode))
                Add(errors, "ai.openAI.apiMode", "The OpenAI API mode is invalid.");

            ValidateHttpUri(errors, "ai.openAI.baseUrl", values.Ai.OpenAI.BaseUrl);
            ValidateHttpUri(errors, "ai.anthropic.baseUrl", values.Ai.Anthropic.BaseUrl);
            if (values.Ai.ExecutionMode == AiExecutionMode.CodexAppServer
                || !string.IsNullOrWhiteSpace(values.Ai.CodexAppServer.Endpoint))
            {
                ValidateWebSocketUri(errors, "ai.codexAppServer.endpoint", values.Ai.CodexAppServer.Endpoint);
                if (Uri.TryCreate(values.Ai.CodexAppServer.Endpoint, UriKind.Absolute, out var codexEndpoint)
                    && !codexEndpoint.IsLoopback
                    && !secrets[RuntimeSecretKeys.CodexToken].IsConfigured)
                    Add(errors, "ai.codexAppServer.token",
                        "A bearer token is required for a remote Codex app-server endpoint.");
            }
            RequireText(errors, "ai.openAI.model", values.Ai.OpenAI.Model);
            RequireText(errors, "ai.anthropic.model", values.Ai.Anthropic.Model);
            RequireText(errors, "ai.anthropic.apiVersion", values.Ai.Anthropic.ApiVersion);
            RequireText(errors, "ai.codexAppServer.permissionProfile",
                values.Ai.CodexAppServer.PermissionProfile);
            RequireRange(errors, "ai.openAI.maxTokens", values.Ai.OpenAI.MaxTokens, 1, int.MaxValue);
            RequireRange(errors, "ai.anthropic.maxTokens", values.Ai.Anthropic.MaxTokens, 1, int.MaxValue);
            RequireRange(errors, "ai.codexAppServer.timeoutSeconds",
                values.Ai.CodexAppServer.TimeoutSeconds, 1, 3600);
        }
        RequireRange(errors, "ai.inference.rateLimitDelayMs",
            values.Ai.Inference.RateLimitDelayMs, 0, int.MaxValue);

        var providerIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var providers = RuntimeAiProviders.GetProviders(values.Ai);
        for (var index = 0; index < providers.Count; index++)
        {
            var provider = providers[index];
            var path = $"ai.providers.{index}";
            if (string.IsNullOrEmpty(provider.Id)
                || !Regex.IsMatch(provider.Id, @"^[a-zA-Z0-9][a-zA-Z0-9_-]{0,63}$", RegexOptions.CultureInvariant))
                Add(errors, path + ".id", "Use 1–64 letters, digits, underscores or hyphens, beginning with a letter or digit.");
            else if (!providerIds.Add(provider.Id))
                Add(errors, path + ".id", "The provider ID is duplicated.");
            RequireText(errors, path + ".name", provider.Name);
            if (!Enum.IsDefined(provider.Protocol))
                Add(errors, path + ".protocol", "The provider protocol is invalid.");
            if (provider.Protocol == AiProviderProtocol.CodexAppServer)
            {
                ValidateWebSocketUri(errors, path + ".endpoint", provider.Endpoint);
                RequireText(errors, path + ".permissionProfile", provider.PermissionProfile);
                RequireRange(errors, path + ".timeoutSeconds", provider.TimeoutSeconds, 1, 3600);
                if (Uri.TryCreate(provider.Endpoint, UriKind.Absolute, out var endpoint)
                    && !endpoint.IsLoopback
                    && secrets.GetValueOrDefault(RuntimeSecretKeys.ProviderToken(provider.Id))?.IsConfigured != true)
                    Add(errors, path + ".token", "A bearer token is required for a remote Codex app-server endpoint.");
            }
            else
            {
                ValidateHttpUri(errors, path + ".baseUrl", provider.BaseUrl);
                RequireText(errors, path + ".model", provider.Model);
                RequireRange(errors, path + ".maxTokens", provider.MaxTokens, 1, int.MaxValue);
                if (provider.Protocol == AiProviderProtocol.Anthropic)
                    RequireText(errors, path + ".apiVersion", provider.ApiVersion);
            }
            var modelIds = new HashSet<string>(StringComparer.Ordinal);
            for (var modelIndex = 0; modelIndex < provider.Models.Count; modelIndex++)
            {
                var model = provider.Models[modelIndex];
                var modelPath = $"{path}.models.{modelIndex}";
                RequireText(errors, modelPath + ".id", model.Id);
                if (!modelIds.Add(model.Id))
                    Add(errors, modelPath + ".id", "The model ID is duplicated.");
                if (model.ReasoningEfforts.Any(effort => !IsReasoningEffort(effort)))
                    Add(errors, modelPath + ".reasoningEfforts", "The reasoning effort is invalid.");
            }
            ValidateProviderEffort(errors, path + ".reasoningEffort", provider, provider.Model, provider.ReasoningEffort);
        }
        var defaultProviderId = RuntimeAiProviders.DefaultProviderId(values.Ai);
        if (providers.Count > 0 && (string.IsNullOrWhiteSpace(defaultProviderId) || !providerIds.Contains(defaultProviderId)))
            Add(errors, "ai.defaultProviderId", "Select an existing default provider.");
        if (providers.Count == 0 && !string.IsNullOrEmpty(defaultProviderId))
            Add(errors, "ai.defaultProviderId", "The default provider does not exist.");
        if (values.Ai.Inference.ProviderId is { Length: > 0 } inferenceProviderId
            && !providerIds.Contains(inferenceProviderId))
            Add(errors, "ai.inference.providerId", "The inference provider does not exist.");
        var inferenceProvider = providers.FirstOrDefault(provider => string.Equals(provider.Id,
            values.Ai.Inference.ProviderId ?? defaultProviderId, StringComparison.OrdinalIgnoreCase));
        if (inferenceProvider is not null)
            ValidateProviderEffort(errors, "ai.inference.reasoningEffort", inferenceProvider,
                values.Ai.Inference.Model ?? inferenceProvider.Model, values.Ai.Inference.ReasoningEffort);
        else
            ValidateReasoningEffort(errors, "ai.inference.reasoningEffort", values.Ai.Inference.ReasoningEffort);

        ValidateHttpUri(errors, "torrent.url", values.Torrent.Url);
        ValidateUserAgent(errors, "torrent.userAgent", values.Torrent.UserAgent);

        var normalizedRoots = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < values.MediaLibrary.AllowedRoots.Count; index++)
        {
            var root = values.MediaLibrary.AllowedRoots[index];
            if (string.IsNullOrWhiteSpace(root) || !Path.IsPathFullyQualified(root))
            {
                Add(errors, $"mediaLibrary.allowedRoots.{index}",
                    "The allowed root must be an absolute server path.");
                continue;
            }

            string normalized;
            try
            {
                normalized = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
            }
            catch (Exception exception) when (exception is ArgumentException or NotSupportedException)
            {
                Add(errors, $"mediaLibrary.allowedRoots.{index}", "The allowed root is invalid.");
                continue;
            }

            if (!normalizedRoots.Add(normalized))
                Add(errors, $"mediaLibrary.allowedRoots.{index}", "The allowed root is duplicated.");
        }

        RequirePositive(errors, "mediaLibrary.scanInterval", values.MediaLibrary.ScanInterval);
        RequireNonNegative(errors, "mediaLibrary.settlingPeriod", values.MediaLibrary.SettlingPeriod);
        RequireNonNegative(errors, "mediaLibrary.missingGracePeriod", values.MediaLibrary.MissingGracePeriod);

        RequirePositive(errors, "incidents.downloadStalledAfter", values.Incidents.DownloadStalledAfter);
        RequirePositive(errors, "incidents.reportThrottle", values.Incidents.ReportThrottle);
        if (values.Incidents.ReconciliationInterval < TimeSpan.FromSeconds(10))
            Add(errors, "incidents.reconciliationInterval", "The interval must be at least 10 seconds.");
        if (values.Incidents.Disk.MinimumAvailableBytes < 0)
            Add(errors, "incidents.disk.minimumAvailableBytes", "The value cannot be negative.");
        if (double.IsNaN(values.Incidents.Disk.MinimumAvailablePercent)
            || double.IsInfinity(values.Incidents.Disk.MinimumAvailablePercent)
            || values.Incidents.Disk.MinimumAvailablePercent is < 0 or > 100)
            Add(errors, "incidents.disk.minimumAvailablePercent", "The percentage must be between 0 and 100.");

        RequireRange(errors, "nfs.port", values.Nfs.Port, 0, 65535);
        if (!IPAddress.TryParse(values.Nfs.BindAddress, out _))
            Add(errors, "nfs.bindAddress", "The bind address must be an IP address.");
        RequireRange(errors, "nfs.leaseSeconds", values.Nfs.LeaseSeconds, 1, int.MaxValue);
        RequireRange(errors, "nfs.maxConnections", values.Nfs.MaxConnections, 1, int.MaxValue);
        RequireRange(errors, "nfs.idleTimeoutSeconds", values.Nfs.IdleTimeoutSeconds, 1, 3600);
        if (values.Nfs.AllowedNetworks.Count == 0)
            Add(errors, "nfs.allowedNetworks", "At least one non-empty CIDR is required.");
        for (var index = 0; index < values.Nfs.AllowedNetworks.Count; index++)
        {
            var network = values.Nfs.AllowedNetworks[index];
            if (string.IsNullOrWhiteSpace(network))
            {
                Add(errors, $"nfs.allowedNetworks.{index}", "The value must be a valid IPv4 or IPv6 CIDR.");
                continue;
            }
            if (!IpCidrRange.TryParse(network, requirePrefix: true, out _))
                Add(errors, $"nfs.allowedNetworks.{index}", "The value must be a valid IPv4 or IPv6 CIDR.");
        }

        if (values.Notifications.Events.Count == 0)
            Add(errors, "notifications.events", "Select at least one notification event.");
        if (values.Notifications.Events.Any(type => !Enum.IsDefined(type) || type == NotificationEventType.Test))
            Add(errors, "notifications.events", "The notification event selection is invalid.");
        if (values.Notifications.QuietHoursStart.HasValue != values.Notifications.QuietHoursEnd.HasValue)
            Add(errors, "notifications.quietHours", "Both quiet-hour boundaries are required.");
        if (values.Notifications.QuietHoursStart is { } start
            && (start < TimeSpan.Zero || start >= TimeSpan.FromDays(1)))
            Add(errors, "notifications.quietHours.start", "The time must be within one day.");
        if (values.Notifications.QuietHoursEnd is { } end
            && (end < TimeSpan.Zero || end >= TimeSpan.FromDays(1)))
            Add(errors, "notifications.quietHours.end", "The time must be within one day.");
        try
        {
            _ = TimeZoneInfo.FindSystemTimeZoneById(values.Notifications.TimeZoneId);
        }
        catch (TimeZoneNotFoundException)
        {
            Add(errors, "notifications.quietHours.timeZoneId", "The time zone is unknown to the server.");
        }
        catch (InvalidTimeZoneException)
        {
            Add(errors, "notifications.quietHours.timeZoneId", "The time zone is invalid.");
        }
        if (values.Notifications.WebhookEnabled
            && !secrets[RuntimeSecretKeys.NotificationWebhookUrl].IsConfigured)
            Add(errors, "notifications.webhook.url", "A webhook URL is required when the channel is enabled.");
        if (secrets[RuntimeSecretKeys.NotificationWebhookUrl] is { IsConfigured: true, Value: { } webhookUrl })
            ValidateWebhookUri(errors, "notifications.webhook.url", webhookUrl);

        var vapidPrivateKey = secrets[RuntimeSecretKeys.NotificationVapidPrivateKey];
        var hasVapidPublicKey = !string.IsNullOrWhiteSpace(values.Notifications.VapidPublicKey);
        if (values.Notifications.WebPushEnabled)
        {
            if (!IsValidVapidSubject(values.Notifications.WebPushSubject))
                Add(errors, "notifications.webPush.subject",
                    "The VAPID subject must be a contact mailto: URI or an HTTPS URL.");
            if (!hasVapidPublicKey || !vapidPrivateKey.IsConfigured)
                Add(errors, "notifications.webPush.vapidKeys",
                    "A VAPID key pair is required when Web Push is enabled.");
        }
        if (hasVapidPublicKey != vapidPrivateKey.IsConfigured)
            Add(errors, "notifications.webPush.vapidKeys",
                "The VAPID public and private keys must be configured together.");
        if (hasVapidPublicKey && vapidPrivateKey is { IsConfigured: true, Value: { } privateKey })
            ValidateVapidKeyPair(
                errors,
                values.Notifications.VapidPublicKey,
                privateKey);

        foreach (var key in secrets.Keys)
        {
            if (secrets.TryGetValue(key, out var secret)
                && secret.IsConfigured
                && string.IsNullOrEmpty(secret.Value))
                Add(errors, SecretPath(key), "The configured secret cannot be empty.");
        }

        return errors.ToDictionary(pair => pair.Key, pair => pair.Value.ToArray(), StringComparer.Ordinal);
    }

    private static void ValidateProviderEffort(
        Dictionary<string, List<string>> errors,
        string key,
        AiProviderSettingsValues provider,
        string model,
        string? effort)
    {
        if (effort is null || !Enum.IsDefined(provider.Protocol))
            return;
        var configured = provider.Models.FirstOrDefault(item => item.Id == model)?.ReasoningEfforts;
        var supported = configured ?? AIModelCapabilities.GetReasoningEfforts(
            Enum.Parse<AI.Configuration.AIProviderProtocol>(provider.Protocol.ToString()), model);
        // Codex can advertise additional capabilities when its model catalog is fetched.
        if (provider.Protocol == AiProviderProtocol.CodexAppServer && configured is null && supported.Count == 0)
        {
            ValidateReasoningEffort(errors, key, effort);
            return;
        }
        if (!supported.Contains(effort, StringComparer.Ordinal))
            Add(errors, key, $"Model '{model}' does not support reasoning effort '{effort}'.");
    }

    private static bool IsReasoningEffort(string effort) =>
        effort is "none" or "minimal" or "low" or "medium" or "high" or "xhigh" or "max";

    private static void ValidateReasoningEffort(
        Dictionary<string, List<string>> errors,
        string key,
        string? effort)
    {
        if (effort is not null && !IsReasoningEffort(effort))
            Add(errors, key, "The reasoning effort is invalid.");
    }

    private static void ValidateHttpUri(
        Dictionary<string, List<string>> errors,
        string key,
        string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            || !string.IsNullOrEmpty(uri.UserInfo)
            || !string.IsNullOrEmpty(uri.Query)
            || !string.IsNullOrEmpty(uri.Fragment))
            Add(errors, key,
                "The endpoint must be an absolute HTTP or HTTPS URL without user information, query, or fragment.");
    }

    private static void ValidateWebSocketUri(
        Dictionary<string, List<string>> errors,
        string key,
        string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || (uri.Scheme != "ws" && uri.Scheme != "wss")
            || !string.IsNullOrEmpty(uri.UserInfo)
            || !string.IsNullOrEmpty(uri.Query)
            || !string.IsNullOrEmpty(uri.Fragment))
        {
            Add(errors, key,
                "The endpoint must be an absolute ws or wss URL without user information, query, or fragment.");
            return;
        }

        if (uri.Scheme == "ws" && !uri.IsLoopback)
            Add(errors, key,
                "Plain ws is allowed only for loopback app-server endpoints; use wss for remote endpoints.");
    }

    private static void ValidateWebhookUri(
        Dictionary<string, List<string>> errors,
        string key,
        string value)
    {
        if (value.Length > 2048)
        {
            Add(errors, key, "The webhook URL cannot exceed 2048 characters.");
            return;
        }
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            || !string.IsNullOrEmpty(uri.UserInfo)
            || !string.IsNullOrEmpty(uri.Fragment))
        {
            Add(errors, key,
                "The webhook must be an absolute HTTP or HTTPS URL without user information or a fragment.");
            return;
        }
        if (uri.Scheme == Uri.UriSchemeHttp && !uri.IsLoopback)
            Add(errors, key, "Plain HTTP is allowed only for loopback webhook endpoints; use HTTPS remotely.");
    }

    private static bool IsValidVapidSubject(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri))
            return false;
        if (uri.Scheme == Uri.UriSchemeHttps)
            return !string.IsNullOrWhiteSpace(uri.IdnHost)
                   && string.IsNullOrEmpty(uri.UserInfo)
                   && string.IsNullOrEmpty(uri.Fragment);
        return uri.Scheme == Uri.UriSchemeMailto
               && value.Length > "mailto:".Length
               && string.IsNullOrEmpty(uri.Fragment);
    }

    private static void ValidateVapidKeyPair(
        Dictionary<string, List<string>> errors,
        string publicKey,
        string privateKey)
    {
        if (!TryDecodeBase64Url(publicKey, out var publicBytes)
            || publicBytes.Length != 65
            || publicBytes[0] != 0x04
            || !TryDecodeBase64Url(privateKey, out var privateBytes)
            || privateBytes.Length != 32)
        {
            Add(errors, "notifications.webPush.vapidKeys", "The VAPID key pair is invalid.");
            return;
        }

        try
        {
            using var ecdsa = ECDsa.Create(new ECParameters
            {
                Curve = ECCurve.NamedCurves.nistP256,
                D = privateBytes
            });
            var derived = ecdsa.ExportParameters(includePrivateParameters: false);
            if (derived.Q.X is null
                || derived.Q.Y is null
                || !CryptographicOperations.FixedTimeEquals(
                    publicBytes.AsSpan(1, 32), derived.Q.X)
                || !CryptographicOperations.FixedTimeEquals(
                    publicBytes.AsSpan(33, 32), derived.Q.Y))
                Add(errors, "notifications.webPush.vapidKeys", "The VAPID public and private keys do not match.");
        }
        catch (Exception exception) when (
            exception is CryptographicException or ArgumentException)
        {
            Add(errors, "notifications.webPush.vapidKeys", "The VAPID key pair is invalid.");
        }
    }

    private static bool TryDecodeBase64Url(string value, out byte[] bytes)
    {
        try
        {
            var normalized = value.Replace('-', '+').Replace('_', '/');
            normalized = normalized.PadRight((normalized.Length + 3) / 4 * 4, '=');
            bytes = Convert.FromBase64String(normalized);
            return true;
        }
        catch (FormatException)
        {
            bytes = [];
            return false;
        }
    }

    private static void ValidateUserAgent(
        Dictionary<string, List<string>> errors,
        string key,
        string? value)
    {
        if (value is null) return;

        using var request = new HttpRequestMessage();
        if (!request.Headers.UserAgent.TryParseAdd(value))
            Add(errors, key, "The value must use valid User-Agent header syntax.");
    }

    private static void RequireText(
        Dictionary<string, List<string>> errors,
        string key,
        string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            Add(errors, key, "The value is required.");
    }

    private static void RequireRange(
        Dictionary<string, List<string>> errors,
        string key,
        int value,
        int minimum,
        int maximum)
    {
        if (value < minimum || value > maximum)
            Add(errors, key, $"The value must be between {minimum} and {maximum}.");
    }

    private static void RequirePositive(
        Dictionary<string, List<string>> errors,
        string key,
        TimeSpan value)
    {
        if (value <= TimeSpan.Zero)
            Add(errors, key, "The duration must be greater than zero.");
    }

    private static void RequireNonNegative(
        Dictionary<string, List<string>> errors,
        string key,
        TimeSpan value)
    {
        if (value < TimeSpan.Zero)
            Add(errors, key, "The duration cannot be negative.");
    }

    private static string SecretPath(string key) => key switch
    {
        RuntimeSecretKeys.OpenAiApiKey => "ai.openAI.apiKey",
        RuntimeSecretKeys.AnthropicApiKey => "ai.anthropic.apiKey",
        RuntimeSecretKeys.CodexToken => "ai.codexAppServer.token",
        RuntimeSecretKeys.TmdbApiKey => "tmdb.apiKey",
        RuntimeSecretKeys.TorrentPassword => "torrent.password",
        RuntimeSecretKeys.NotificationWebhookUrl => "notifications.webhook.url",
        RuntimeSecretKeys.NotificationVapidPrivateKey => "notifications.webPush.vapidPrivateKey",
        _ => key
    };

    private static void Add(
        Dictionary<string, List<string>> errors,
        string key,
        string error)
    {
        if (!errors.TryGetValue(key, out var values))
        {
            values = [];
            errors[key] = values;
        }

        values.Add(error);
    }
}

internal static class RuntimeSettingsFlattener
{
    public static IReadOnlyDictionary<string, string?> Flatten(
        RuntimeSettingsValues values,
        IReadOnlyDictionary<string, ResolvedSecret> secrets,
        int allowedRootSlotCount)
    {
        var flattened = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        var ai = values.Ai;
        flattened["AI:Engine"] = ai.ExecutionMode.ToString();
        flattened["AI:Provider"] = ai.Provider.ToString();
        flattened["AI:OpenAI:BaseUrl"] = ai.OpenAI.BaseUrl;
        flattened["AI:OpenAI:ApiMode"] = ai.OpenAI.ApiMode.ToString();
        flattened["AI:OpenAI:Model"] = ai.OpenAI.Model;
        flattened["AI:OpenAI:MaxTokens"] = ai.OpenAI.MaxTokens.ToString(CultureInfo.InvariantCulture);
        flattened["AI:Anthropic:BaseUrl"] = ai.Anthropic.BaseUrl;
        flattened["AI:Anthropic:Model"] = ai.Anthropic.Model;
        flattened["AI:Anthropic:MaxTokens"] = ai.Anthropic.MaxTokens.ToString(CultureInfo.InvariantCulture);
        flattened["AI:Anthropic:ApiVersion"] = ai.Anthropic.ApiVersion;
        flattened["AI:CodexAppServer:Endpoint"] = ai.CodexAppServer.Endpoint;
        flattened["AI:CodexAppServer:Model"] = ai.CodexAppServer.Model;
        flattened["AI:CodexAppServer:PermissionProfile"] = ai.CodexAppServer.PermissionProfile;
        flattened["AI:CodexAppServer:TimeoutSeconds"] =
            ai.CodexAppServer.TimeoutSeconds.ToString(CultureInfo.InvariantCulture);
        flattened["Inference:RateLimitDelayMs"] =
            ai.Inference.RateLimitDelayMs.ToString(CultureInfo.InvariantCulture);

        flattened["AI:ProvidersConfigured"] = bool.TrueString;
        flattened["AI:DefaultProviderId"] = RuntimeAiProviders.DefaultProviderId(ai);
        flattened["Inference:ProviderId"] = ai.Inference.ProviderId;
        flattened["Inference:Model"] = ai.Inference.Model;
        flattened["Inference:ReasoningEffort"] = ai.Inference.ReasoningEffort;
        foreach (var provider in RuntimeAiProviders.GetProviders(ai))
        {
            var prefix = $"AI:Providers:{provider.Id}";
            flattened[prefix + ":Id"] = provider.Id;
            flattened[prefix + ":Name"] = provider.Name;
            flattened[prefix + ":Protocol"] = provider.Protocol.ToString();
            flattened[prefix + ":BaseUrl"] = provider.BaseUrl;
            flattened[prefix + ":Model"] = provider.Model;
            flattened[prefix + ":MaxTokens"] = provider.MaxTokens.ToString(CultureInfo.InvariantCulture);
            flattened[prefix + ":ReasoningEffort"] = provider.ReasoningEffort;
            flattened[prefix + ":ApiVersion"] = provider.ApiVersion;
            flattened[prefix + ":Endpoint"] = provider.Endpoint;
            flattened[prefix + ":PermissionProfile"] = provider.PermissionProfile;
            flattened[prefix + ":TimeoutSeconds"] = provider.TimeoutSeconds.ToString(CultureInfo.InvariantCulture);
            for (var index = 0; index < provider.Models.Count; index++)
            {
                var model = provider.Models[index];
                flattened[$"{prefix}:Models:{index}:Id"] = model.Id;
                flattened[$"{prefix}:Models:{index}:Name"] = model.Name;
                flattened[$"{prefix}:Models:{index}:ReasoningEffortsConfigured"] = bool.TrueString;
                for (var effortIndex = 0; effortIndex < model.ReasoningEfforts.Count; effortIndex++)
                    flattened[$"{prefix}:Models:{index}:ReasoningEfforts:{effortIndex}"] = model.ReasoningEfforts[effortIndex];
            }
        }

        flattened["Torrent:Remote:Url"] = values.Torrent.Url;
        flattened["Torrent:Remote:UserName"] = values.Torrent.UserName;
        flattened["Torrent:Remote:UserAgent"] = values.Torrent.UserAgent;

        for (var index = 0; index < values.MediaLibrary.AllowedRoots.Count; index++)
            flattened[$"MediaLibrary:AllowedRoots:{index}"] = values.MediaLibrary.AllowedRoots[index];
        for (var index = values.MediaLibrary.AllowedRoots.Count; index < allowedRootSlotCount; index++)
            flattened[$"MediaLibrary:AllowedRoots:{index}"] = null;
        flattened["MediaLibrary:ScanInterval"] =
            values.MediaLibrary.ScanInterval.ToString("c", CultureInfo.InvariantCulture);
        flattened["MediaLibrary:SettlingPeriod"] =
            values.MediaLibrary.SettlingPeriod.ToString("c", CultureInfo.InvariantCulture);
        flattened["MediaLibrary:MissingGracePeriod"] =
            values.MediaLibrary.MissingGracePeriod.ToString("c", CultureInfo.InvariantCulture);

        flattened["Incidents:DownloadStalledAfter"] =
            values.Incidents.DownloadStalledAfter.ToString("c", CultureInfo.InvariantCulture);
        flattened["Incidents:ReportThrottle"] =
            values.Incidents.ReportThrottle.ToString("c", CultureInfo.InvariantCulture);
        flattened["Incidents:ReconciliationInterval"] =
            values.Incidents.ReconciliationInterval.ToString("c", CultureInfo.InvariantCulture);
        flattened["Incidents:Disk:MinimumAvailableBytes"] =
            values.Incidents.Disk.MinimumAvailableBytes.ToString(CultureInfo.InvariantCulture);
        flattened["Incidents:Disk:MinimumAvailablePercent"] =
            values.Incidents.Disk.MinimumAvailablePercent.ToString(CultureInfo.InvariantCulture);

        flattened["Nfs:Enabled"] = values.Nfs.Enabled.ToString(CultureInfo.InvariantCulture);
        flattened["Nfs:Port"] = values.Nfs.Port.ToString(CultureInfo.InvariantCulture);
        flattened["Nfs:BindAddress"] = values.Nfs.BindAddress;
        flattened["Nfs:LeaseSeconds"] = values.Nfs.LeaseSeconds.ToString(CultureInfo.InvariantCulture);
        flattened["Nfs:MaxConnections"] = values.Nfs.MaxConnections.ToString(CultureInfo.InvariantCulture);
        flattened["Nfs:IdleTimeoutSeconds"] =
            values.Nfs.IdleTimeoutSeconds.ToString(CultureInfo.InvariantCulture);
        flattened["Nfs:AllowAnonymous"] = values.Nfs.AllowAnonymous.ToString(CultureInfo.InvariantCulture);
        for (var index = 0; index < values.Nfs.AllowedNetworks.Count; index++)
            flattened[$"Nfs:AllowedNetworks:{index}"] = values.Nfs.AllowedNetworks[index];

        flattened["Notifications:Webhook:Enabled"] =
            values.Notifications.WebhookEnabled.ToString(CultureInfo.InvariantCulture);
        flattened["Notifications:WebPush:Enabled"] =
            values.Notifications.WebPushEnabled.ToString(CultureInfo.InvariantCulture);
        flattened["Notifications:WebPush:Subject"] = values.Notifications.WebPushSubject;
        flattened["Notifications:WebPush:VapidPublicKey"] = values.Notifications.VapidPublicKey;
        flattened["Notifications:Events"] = string.Join(',', values.Notifications.Events);
        flattened["Notifications:QuietHours:Start"] =
            values.Notifications.QuietHoursStart?.ToString("c", CultureInfo.InvariantCulture);
        flattened["Notifications:QuietHours:End"] =
            values.Notifications.QuietHoursEnd?.ToString("c", CultureInfo.InvariantCulture);
        flattened["Notifications:QuietHours:TimeZone"] = values.Notifications.TimeZoneId;

        foreach (var key in RuntimeSecretKeys.All.Concat(RuntimeSecretKeys.ForProviders(ai)))
            flattened[key] = secrets.TryGetValue(key, out var secret) && secret.IsConfigured
                ? secret.Value
                : string.Empty;

        return flattened;
    }
}
