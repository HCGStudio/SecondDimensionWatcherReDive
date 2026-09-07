using System.ComponentModel.DataAnnotations;
using SecondDimensionWatcherReDive.Configuration;
using SecondDimensionWatcherReDive.Framework.Notifications;

namespace SecondDimensionWatcherReDive.Controllers.External;

internal sealed record SecretStateResponse(
    bool IsConfigured,
    SecretConfigurationSource Source);

internal sealed record OpenAiSettingsResponse(
    string BaseUrl,
    OpenAiApiMode ApiMode,
    string Model,
    int MaxTokens,
    SecretStateResponse ApiKey);

internal sealed record AnthropicSettingsResponse(
    string BaseUrl,
    string Model,
    int MaxTokens,
    string ApiVersion,
    SecretStateResponse ApiKey);

internal sealed record CodexAppServerSettingsResponse(
    string Endpoint,
    string Model,
    string PermissionProfile,
    int TimeoutSeconds,
    SecretStateResponse Token);

internal sealed record InferenceSettingsResponse(int RateLimitDelayMs);

internal sealed record AiSettingsResponse(
    AiExecutionMode ExecutionMode,
    BuiltInAiProvider Provider,
    OpenAiSettingsResponse OpenAI,
    AnthropicSettingsResponse Anthropic,
    CodexAppServerSettingsResponse CodexAppServer,
    InferenceSettingsResponse Inference);

internal sealed record TmdbSettingsResponse(SecretStateResponse ApiKey);

internal sealed record TorrentSettingsResponse(
    string Url,
    string UserName,
    string UserAgent,
    SecretStateResponse Password);

internal sealed record MediaLibrarySettingsResponse(
    IReadOnlyList<string> AllowedRoots,
    TimeSpan ScanInterval,
    TimeSpan SettlingPeriod,
    TimeSpan MissingGracePeriod);

internal sealed record IncidentDiskSettingsResponse(
    long MinimumAvailableBytes,
    double MinimumAvailablePercent);

internal sealed record IncidentSettingsResponse(
    TimeSpan DownloadStalledAfter,
    TimeSpan ReportThrottle,
    TimeSpan ReconciliationInterval,
    IncidentDiskSettingsResponse Disk);

internal sealed record NfsSettingsResponse(
    bool Enabled,
    int Port,
    string BindAddress,
    int LeaseSeconds,
    int MaxConnections,
    int IdleTimeoutSeconds,
    bool AllowAnonymous,
    IReadOnlyList<string> AllowedNetworks,
    bool RestartRequired,
    bool PendingRestart);

internal sealed record NotificationSettingsResponse(
    bool WebhookEnabled,
    bool WebPushEnabled,
    string WebPushSubject,
    string VapidPublicKey,
    SecretStateResponse VapidPrivateKey,
    IReadOnlyList<NotificationEventType> Events,
    TimeSpan? QuietHoursStart,
    TimeSpan? QuietHoursEnd,
    string TimeZoneId,
    SecretStateResponse WebhookUrl);

internal sealed record ApplicationSettingsResponse(
    long Revision,
    bool PendingRestart,
    AiSettingsResponse Ai,
    TmdbSettingsResponse Tmdb,
    TorrentSettingsResponse Torrent,
    MediaLibrarySettingsResponse MediaLibrary,
    IncidentSettingsResponse Incidents,
    NfsSettingsResponse Nfs,
    NotificationSettingsResponse Notifications);

internal sealed record SecretMutationRequest(
    [Required] SecretMutationOperation? Operation,
    string? Value);

internal sealed record OpenAiSettingsPatchRequest(
    [Required] string? BaseUrl,
    [Required] OpenAiApiMode? ApiMode,
    [Required] string? Model,
    [Required] int? MaxTokens,
    SecretMutationRequest? ApiKey);

internal sealed record AnthropicSettingsPatchRequest(
    [Required] string? BaseUrl,
    [Required] string? Model,
    [Required] int? MaxTokens,
    [Required] string? ApiVersion,
    SecretMutationRequest? ApiKey);

internal sealed record CodexAppServerSettingsPatchRequest(
    [Required] string? Endpoint,
    string? Model,
    [Required] string? PermissionProfile,
    [Required] int? TimeoutSeconds,
    SecretMutationRequest? Token);

internal sealed record InferenceSettingsPatchRequest(
    [Required] int? RateLimitDelayMs);

internal sealed record AiSettingsPatchRequest(
    [Required] AiExecutionMode? ExecutionMode,
    [Required] BuiltInAiProvider? Provider,
    [Required] OpenAiSettingsPatchRequest? OpenAI,
    [Required] AnthropicSettingsPatchRequest? Anthropic,
    [Required] CodexAppServerSettingsPatchRequest? CodexAppServer,
    [Required] InferenceSettingsPatchRequest? Inference);

internal sealed record TmdbSettingsPatchRequest(SecretMutationRequest? ApiKey);

internal sealed record TorrentSettingsPatchRequest(
    [Required] string? Url,
    string? UserName,
    string? UserAgent,
    SecretMutationRequest? Password);

internal sealed record MediaLibrarySettingsPatchRequest(
    [Required] IReadOnlyList<string>? AllowedRoots,
    [Required] TimeSpan? ScanInterval,
    [Required] TimeSpan? SettlingPeriod,
    [Required] TimeSpan? MissingGracePeriod);

internal sealed record IncidentDiskSettingsPatchRequest(
    [Required] long? MinimumAvailableBytes,
    [Required] double? MinimumAvailablePercent);

internal sealed record IncidentSettingsPatchRequest(
    [Required] TimeSpan? DownloadStalledAfter,
    [Required] TimeSpan? ReportThrottle,
    [Required] TimeSpan? ReconciliationInterval,
    [Required] IncidentDiskSettingsPatchRequest? Disk);

internal sealed record NfsSettingsPatchRequest(
    [Required] bool? Enabled,
    [Required] int? Port,
    [Required] string? BindAddress,
    [Required] int? LeaseSeconds,
    [Required] int? MaxConnections,
    [Required] int? IdleTimeoutSeconds,
    [Required] bool? AllowAnonymous,
    [Required] IReadOnlyList<string>? AllowedNetworks);

internal sealed record NotificationSettingsPatchRequest(
    [Required] bool? WebhookEnabled,
    [Required] bool? WebPushEnabled,
    [Required] string? WebPushSubject,
    [Required] IReadOnlyList<NotificationEventType>? Events,
    TimeSpan? QuietHoursStart,
    TimeSpan? QuietHoursEnd,
    [Required] string? TimeZoneId,
    SecretMutationRequest? WebhookUrl,
    bool GenerateVapidKeys = false);

internal sealed record PatchApplicationSettingsRequest(
    long ExpectedRevision,
    AiSettingsPatchRequest? Ai,
    TmdbSettingsPatchRequest? Tmdb,
    TorrentSettingsPatchRequest? Torrent,
    MediaLibrarySettingsPatchRequest? MediaLibrary,
    IncidentSettingsPatchRequest? Incidents,
    NfsSettingsPatchRequest? Nfs,
    NotificationSettingsPatchRequest? Notifications = null);
