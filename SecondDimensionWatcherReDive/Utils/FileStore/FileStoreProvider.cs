namespace SecondDimensionWatcherReDive.Utils.FileStore;

public class FileStoreProvider(IServiceProvider serviceProvider) : IFileStoreProvider
{
    public IFileStore GetRequiredClient(string clientName)
    {
        return serviceProvider.GetServices<IFileStore>().First(c => c.Name == clientName);
    }

    public IFileStore? GetClient(string clientName)
    {
        return serviceProvider.GetServices<IFileStore>().FirstOrDefault(c => c.Name == clientName);
    }
}