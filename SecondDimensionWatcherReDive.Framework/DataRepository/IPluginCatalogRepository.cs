using SecondDimensionWatcherReDive.Framework.Plugin;

namespace SecondDimensionWatcherReDive.Framework.DataRepository;

public sealed record PluginCatalogEntry(
    PluginManifest Manifest,
    bool IsEnabled,
    PluginCapabilities ApprovedCapabilities,
    PluginHealth Health,
    string PackageDirectory,
    string ConfigurationJson,
    int DataVersion,
    string? PublisherFingerprint);

public sealed record RetainedPluginData(
    string Id,
    string ConfigurationJson,
    int DataVersion,
    DateTimeOffset RetainedAt,
    string? PublisherFingerprint);

public interface IPluginCatalogRepository
{
    Task<IReadOnlyList<PluginCatalogEntry>> GetAllAsync(CancellationToken cancellationToken);
    Task<PluginCatalogEntry?> FindAsync(string id, CancellationToken cancellationToken);
    Task SaveAsync(PluginCatalogEntry entry, CancellationToken cancellationToken);
    Task RemoveAsync(string id, CancellationToken cancellationToken);
    Task<RetainedPluginData?> FindRetainedAsync(string id, CancellationToken cancellationToken);
    Task SaveRetainedAsync(RetainedPluginData retained, CancellationToken cancellationToken);
    Task RemoveRetainedAsync(string id, CancellationToken cancellationToken);
}
