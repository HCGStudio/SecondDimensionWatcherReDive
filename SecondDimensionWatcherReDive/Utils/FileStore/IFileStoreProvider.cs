using SecondDimensionWatcherReDive.Framework.FileStore;

namespace SecondDimensionWatcherReDive.Utils.FileStore;

public interface IFileStoreProvider
{
    public IFileStore GetRequiredClient(string clientName);
    public IFileStore? GetClient(string clientName);
}