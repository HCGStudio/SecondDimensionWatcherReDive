using System.Text.Json;
using SecondDimensionWatcherReDive.Framework.Plugin;

namespace SecondDimensionWatcherReDive.PluginPlatform;

internal interface IPluginManager
{
    Task InitializeAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<InstalledPlugin>> GetAllAsync(CancellationToken cancellationToken);
    Task EnableAsync(string id, CancellationToken cancellationToken);
    Task DisableAsync(string id, CancellationToken cancellationToken);
    Task<PluginInstallResult> UpgradeAsync(
        string id,
        string previewToken,
        string expectedSha256,
        PluginCapabilities approvedCapabilities,
        CancellationToken cancellationToken);
    Task UninstallAsync(string id, bool deleteData, CancellationToken cancellationToken);
    Task UpdateConfigurationAsync(string id, JsonElement configuration, CancellationToken cancellationToken);
    Task<JsonElement> InvokeAsync(
        string id,
        string handler,
        JsonElement input,
        CancellationToken cancellationToken);
    IReadOnlyList<InstalledPlugin> GetSnapshot();
}
