using SecondDimensionWatcherReDive.Framework.FileStore;
using SecondDimensionWatcherReDive.PluginPlatform;

namespace SecondDimensionWatcherReDive.Utils.FileStore;

public class FileStoreProvider(
    IServiceProvider serviceProvider,
    IPluginProviderRegistry pluginProviderRegistry) : IFileStoreProvider
{
    public IFileStore GetRequiredClient(string clientName)
    {
        return GetClient(clientName)
               ?? throw new InvalidOperationException($"File store '{clientName}' is not registered.");
    }

    public IFileStore? GetClient(string clientName)
    {
        var matches = GetClients().Where(client => client.Name == clientName).Take(2).ToArray();
        return matches.Length switch
        {
            0 => null,
            1 => matches[0],
            _ => throw new InvalidOperationException(
                $"File store identity '{clientName}' is registered more than once.")
        };
    }

    private IEnumerable<IFileStore> GetClients()
        => serviceProvider.GetServices<IFileStore>().Concat(pluginProviderRegistry.GetFileStores());
}
