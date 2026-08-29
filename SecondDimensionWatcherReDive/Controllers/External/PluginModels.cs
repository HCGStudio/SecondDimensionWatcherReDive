using System.Text.Json;
namespace SecondDimensionWatcherReDive.Controllers.External;

internal sealed record InstallPluginRequest(
    string PreviewToken,
    string ExpectedSha256,
    PluginCapabilities ApprovedCapabilities);

internal sealed record UpdatePluginConfigurationRequest(JsonElement Configuration);

internal sealed record RemotePluginInstallRequest(string Url, string? ExpectedSha256);

internal sealed record PluginOperationError(string Code, string Message);

internal sealed record PluginCapabilities(
    IReadOnlyList<string> NetworkDomains,
    IReadOnlyList<string> FileRoots,
    bool Notifications,
    bool DownloadControl,
    bool StorageAccess,
    bool BackgroundTasks);

internal sealed record PluginDependency(string Id, string MinimumVersion);

internal sealed record PluginProvider(
    string Kind,
    string Name,
    IReadOnlyDictionary<string, string> Handlers);

internal sealed record PluginDataMigration(string Strategy, string? Description);

internal sealed record PluginManifest(
    string Id,
    string Name,
    string Version,
    string ApiVersion,
    string EntryPoint,
    string? Description,
    IReadOnlyList<PluginDependency> Dependencies,
    PluginCapabilities Capabilities,
    IReadOnlyList<string> Platforms,
    IReadOnlyDictionary<string, string> FileSha256,
    string? SignaturePublisher,
    string? SignatureAlgorithm,
    IReadOnlyList<PluginProvider> Providers,
    int DataVersion,
    PluginDataMigration? DataMigration);

internal sealed record PluginHealth(
    string Status,
    int ConsecutiveFailures,
    DateTimeOffset? LastSuccessAt,
    DateTimeOffset? LastFailureAt,
    string? LastError,
    DateTimeOffset? CircuitOpenUntil);

internal sealed record InstalledPlugin(
    PluginManifest Manifest,
    bool IsEnabled,
    PluginCapabilities ApprovedCapabilities,
    IReadOnlyList<string> CompatibilityErrors,
    PluginHealth Health,
    bool HasConfiguration);

internal sealed record PluginPackagePreview(
    string Token,
    string PackageSha256,
    PluginManifest Manifest,
    IReadOnlyList<string> CompatibilityErrors,
    bool IsSignatureTrusted,
    string SignatureStatus,
    DateTimeOffset ExpiresAt);

internal sealed record PluginInstallResult(
    string Id,
    string Version,
    bool IsUpgrade,
    IReadOnlyList<string> CompatibilityErrors);
