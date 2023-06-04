namespace SecondDimensionWatcherReDive.Utils.FileDownload;

public class FileDownloadClientProvider : IFileDownloadClientProvider
{
    private readonly IServiceProvider _serviceProvider;

    public FileDownloadClientProvider(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public IFileDownloadClient GetRequiredClient(string downloadType)
    {
        return _serviceProvider.GetServices<IFileDownloadClient>().First(c => c.FileDownloadType == downloadType);
    }

    public IFileDownloadClient? GetClient(string downloadType)
    {
        return _serviceProvider.GetServices<IFileDownloadClient>()
            .FirstOrDefault(c => c.FileDownloadType == downloadType);
    }

    public IFileDownloadClient GetRequiredClient(string downloadType, string fileStoreType)
    {
        return _serviceProvider.GetServices<IFileDownloadClient>()
            .First(c => c.FileDownloadType == downloadType && c.SupportedFileStoreType == fileStoreType);
    }

    public IFileDownloadClient? GetClient(string downloadType, string fileStoreType)
    {
        return _serviceProvider.GetServices<IFileDownloadClient>()
            .FirstOrDefault(c => c.FileDownloadType == downloadType && c.SupportedFileStoreType == fileStoreType);
    }
}