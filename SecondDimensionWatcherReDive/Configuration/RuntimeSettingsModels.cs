using System.Globalization;
using System.Net;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Configuration;
using SecondDimensionWatcherReDive.Framework.Notifications;

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

internal sealed record InferenceSettingsValues(int RateLimitDelayMs);

internal sealed record AiSettingsValues(
    AiExecutionMode ExecutionMode,
    BuiltInAiProvider Provider,
    OpenAiSettingsValues OpenAI,
    AnthropicSettingsValues Anthropic,
    CodexAppServerSettingsValues CodexAppServer,
    InferenceSettingsValues Inference);

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
    int MaxConnections);

internal sealed record NotificationSettingsValues(
    bool WebhookEnabled,
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
        new(StringComparer.Ordinal);
}

internal sealed record SecretMutation(
    SecretMutationOperation Operation,
    string? Value);

internal sealed record AiSettingsUpdate(
    AiSettingsValues Values,
    SecretMutation? OpenAiApiKey,
    SecretMutation? AnthropicApiKey,
    SecretMutation? CodexToken);

internal sealed record TmdbSettingsUpdate(SecretMutation? ApiKey);

internal sealed record TorrentSettingsUpdate(
    TorrentSettingsValues Values,
    SecretMutation? Password);

internal sealed record NotificationSettingsUpdate(
    NotificationSettingsValues Values,
    SecretMutation? WebhookUrl);

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

    public static readonly string[] All =
    [
        OpenAiApiKey,
        AnthropicApiKey,
        CodexToken,
        TmdbApiKey,
        TorrentPassword,
        NotificationWebhookUrl
    ];
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
                configuration["Nfs:BindAddress"] ?? "0.0.0.0",
                configuration.GetValue<int?>("Nfs:LeaseSeconds") ?? 90,
                configuration.GetValue<int?>("Nfs:MaxConnections") ?? 32),
            new NotificationSettingsValues(
                configuration.GetValue<bool?>("Notifications:Webhook:Enabled") ?? false,
                ReadNotificationEvents(configuration["Notifications:Events"]),
                configuration.GetValue<TimeSpan?>("Notifications:QuietHours:Start"),
                configuration.GetValue<TimeSpan?>("Notifications:QuietHours:End"),
                configuration["Notifications:QuietHours:TimeZone"] ?? "UTC"));

    public static IReadOnlyDictionary<string, string?> ReadDeploymentSecrets(
        IConfiguration configuration) =>
        RuntimeSecretKeys.All.ToDictionary(
            key => key,
            key => NullIfWhiteSpace(configuration[key]),
            StringComparer.Ordinal);

    private static AiSettingsValues ReadAi(IConfiguration configuration) =>
        new(
            ParseEnum(configuration["AI:Engine"], AiExecutionMode.BuiltIn),
            ParseEnum(configuration["AI:Provider"], BuiltInAiProvider.OpenAI),
            new OpenAiSettingsValues(
                configuration["AI:OpenAI:BaseUrl"] ?? "https://api.openai.com/v1",
                ParseEnum(configuration["AI:OpenAI:ApiMode"], OpenAiApiMode.ChatCompletions),
                configuration["AI:OpenAI:Model"] ?? "gpt-4o-mini",
                configuration.GetValue<int?>("AI:OpenAI:MaxTokens") ?? 1024),
            new AnthropicSettingsValues(
                configuration["AI:Anthropic:BaseUrl"] ?? "https://api.anthropic.com",
                configuration["AI:Anthropic:Model"] ?? "claude-sonnet-4-20250514",
                configuration.GetValue<int?>("AI:Anthropic:MaxTokens") ?? 1024,
                configuration["AI:Anthropic:ApiVersion"] ?? "2023-06-01"),
            new CodexAppServerSettingsValues(
                configuration["AI:CodexAppServer:Endpoint"] ?? string.Empty,
                NullIfWhiteSpace(configuration["AI:CodexAppServer:Model"]),
                configuration["AI:CodexAppServer:PermissionProfile"] ?? ":read-only",
                configuration.GetValue<int?>("AI:CodexAppServer:TimeoutSeconds") ?? 300),
            new InferenceSettingsValues(
                configuration.GetValue<int?>("Inference:RateLimitDelayMs") ?? 1000));

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
        RequireRange(errors, "ai.inference.rateLimitDelayMs",
            values.Ai.Inference.RateLimitDelayMs, 0, int.MaxValue);

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

        foreach (var key in RuntimeSecretKeys.All)
        {
            if (secrets.TryGetValue(key, out var secret)
                && secret.IsConfigured
                && string.IsNullOrEmpty(secret.Value))
                Add(errors, SecretPath(key), "The configured secret cannot be empty.");
        }

        return errors.ToDictionary(pair => pair.Key, pair => pair.Value.ToArray(), StringComparer.Ordinal);
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
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            || !string.IsNullOrEmpty(uri.UserInfo)
            || !string.IsNullOrEmpty(uri.Fragment))
            Add(errors, key,
                "The webhook must be an absolute HTTP or HTTPS URL without user information or a fragment.");
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

        flattened["Notifications:Webhook:Enabled"] =
            values.Notifications.WebhookEnabled.ToString(CultureInfo.InvariantCulture);
        flattened["Notifications:Events"] = string.Join(',', values.Notifications.Events);
        flattened["Notifications:QuietHours:Start"] =
            values.Notifications.QuietHoursStart?.ToString("c", CultureInfo.InvariantCulture);
        flattened["Notifications:QuietHours:End"] =
            values.Notifications.QuietHoursEnd?.ToString("c", CultureInfo.InvariantCulture);
        flattened["Notifications:QuietHours:TimeZone"] = values.Notifications.TimeZoneId;

        foreach (var key in RuntimeSecretKeys.All)
            flattened[key] = secrets.TryGetValue(key, out var secret) && secret.IsConfigured
                ? secret.Value
                : string.Empty;

        return flattened;
    }
}
