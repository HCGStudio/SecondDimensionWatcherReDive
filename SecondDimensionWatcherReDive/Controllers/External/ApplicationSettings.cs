using System.ComponentModel.DataAnnotations;
using SecondDimensionWatcherReDive.Configuration;

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

internal sealed record ApplicationSettingsResponse(
    long Revision,
    bool PendingRestart,
    AiSettingsResponse Ai,
    TmdbSettingsResponse Tmdb,
    TorrentSettingsResponse Torrent,
    MediaLibrarySettingsResponse MediaLibrary,
    IncidentSettingsResponse Incidents,
    NfsSettingsResponse Nfs);

internal sealed record SecretMutationRequest(
    [property: Required] SecretMutationOperation? Operation,
    string? Value);

internal sealed record OpenAiSettingsPatchRequest(
    [property: Required] string? BaseUrl,
    [property: Required] OpenAiApiMode? ApiMode,
    [property: Required] string? Model,
    [property: Required] int? MaxTokens,
    SecretMutationRequest? ApiKey);

internal sealed record AnthropicSettingsPatchRequest(
    [property: Required] string? BaseUrl,
    [property: Required] string? Model,
    [property: Required] int? MaxTokens,
    [property: Required] string? ApiVersion,
    SecretMutationRequest? ApiKey);

internal sealed record CodexAppServerSettingsPatchRequest(
    [property: Required] string? Endpoint,
    string? Model,
    [property: Required] string? PermissionProfile,
    [property: Required] int? TimeoutSeconds,
    SecretMutationRequest? Token);

internal sealed record InferenceSettingsPatchRequest(
    [property: Required] int? RateLimitDelayMs);

internal sealed record AiSettingsPatchRequest(
    [property: Required] AiExecutionMode? ExecutionMode,
    [property: Required] BuiltInAiProvider? Provider,
    [property: Required] OpenAiSettingsPatchRequest? OpenAI,
    [property: Required] AnthropicSettingsPatchRequest? Anthropic,
    [property: Required] CodexAppServerSettingsPatchRequest? CodexAppServer,
    [property: Required] InferenceSettingsPatchRequest? Inference);

internal sealed record TmdbSettingsPatchRequest(SecretMutationRequest? ApiKey);

internal sealed record TorrentSettingsPatchRequest(
    [property: Required] string? Url,
    string? UserName,
    string? UserAgent,
    SecretMutationRequest? Password);

internal sealed record MediaLibrarySettingsPatchRequest(
    [property: Required] IReadOnlyList<string>? AllowedRoots,
    [property: Required] TimeSpan? ScanInterval,
    [property: Required] TimeSpan? SettlingPeriod,
    [property: Required] TimeSpan? MissingGracePeriod);

internal sealed record IncidentDiskSettingsPatchRequest(
    [property: Required] long? MinimumAvailableBytes,
    [property: Required] double? MinimumAvailablePercent);

internal sealed record IncidentSettingsPatchRequest(
    [property: Required] TimeSpan? DownloadStalledAfter,
    [property: Required] TimeSpan? ReportThrottle,
    [property: Required] TimeSpan? ReconciliationInterval,
    [property: Required] IncidentDiskSettingsPatchRequest? Disk);

internal sealed record NfsSettingsPatchRequest(
    [property: Required] bool? Enabled,
    [property: Required] int? Port,
    [property: Required] string? BindAddress,
    [property: Required] int? LeaseSeconds,
    [property: Required] int? MaxConnections,
    [property: Required] int? IdleTimeoutSeconds,
    [property: Required] bool? AllowAnonymous,
    [property: Required] IReadOnlyList<string>? AllowedNetworks);

internal sealed record PatchApplicationSettingsRequest(
    long ExpectedRevision,
    AiSettingsPatchRequest? Ai,
    TmdbSettingsPatchRequest? Tmdb,
    TorrentSettingsPatchRequest? Torrent,
    MediaLibrarySettingsPatchRequest? MediaLibrary,
    IncidentSettingsPatchRequest? Incidents,
    NfsSettingsPatchRequest? Nfs);
