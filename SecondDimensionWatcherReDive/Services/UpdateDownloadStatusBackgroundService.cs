using System.Threading.Channels;
using Microsoft.Extensions.Caching.Memory;
using SecondDimensionWatcherReDive.Data;

namespace SecondDimensionWatcherReDive.Services;

public class UpdateDownloadStatusBackgroundService : BackgroundService
{
    private readonly Channel<FileDownloadStatus> _fileDownloadStatus;
    private readonly IMemoryCache _memoryCache;

    public UpdateDownloadStatusBackgroundService(Channel<FileDownloadStatus> fileDownloadStatus, IMemoryCache memoryCache)
    {
        _fileDownloadStatus = fileDownloadStatus;
        _memoryCache = memoryCache;
    }

    protected override async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        var reader = _fileDownloadStatus.Reader;
        while (!cancellationToken.IsCancellationRequested)
        {
            var status = await reader.ReadAsync(cancellationToken);
            if (status.State == FileDownloadState.Finished)
                _memoryCache.Set(status.ItemId, status, TimeSpan.FromMinutes(5));
            else
                _memoryCache.Set(status.ItemId, status);
        }
    }
}