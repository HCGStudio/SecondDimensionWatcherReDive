using SecondDimensionWatcherReDive.Framework.FileDownload;

namespace SecondDimensionWatcherReDive.Utils.FileDownload;

public class FileDownloadClientProvider(IServiceProvider serviceProvider) : IFileDownloadClientProvider
{
    public IFileDownloadClient GetRequiredClient(string downloadType)
    {
        return serviceProvider.GetServices<IFileDownloadClient>().First(c => c.FileDownloadType == downloadType);
    }

    public IFileDownloadClient? GetClient(string downloadType)
    {
        return serviceProvider.GetServices<IFileDownloadClient>()
            .FirstOrDefault(c => c.FileDownloadType == downloadType);
    }

    public IFileDownloadClient GetRequiredClient(string downloadType, string fileStoreType)
    {
        return serviceProvider.GetServices<IFileDownloadClient>()
            .First(c => c.FileDownloadType == downloadType && c.SupportedFileStoreType == fileStoreType);
    }

    public IFileDownloadClient? GetClient(string downloadType, string fileStoreType)
    {
        return serviceProvider.GetServices<IFileDownloadClient>()
            .FirstOrDefault(c => c.FileDownloadType == downloadType && c.SupportedFileStoreType == fileStoreType);
    }
}