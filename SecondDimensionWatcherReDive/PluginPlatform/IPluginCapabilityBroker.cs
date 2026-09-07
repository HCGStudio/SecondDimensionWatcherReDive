using System.Text.Json;
using SecondDimensionWatcherReDive.Framework.DataRepository;

namespace SecondDimensionWatcherReDive.PluginPlatform;

internal interface IPluginCapabilityBroker
{
    Task<JsonElement> ExecuteAsync(
        PluginCatalogEntry plugin,
        string capability,
        JsonElement payload,
        CancellationToken cancellationToken);
}
