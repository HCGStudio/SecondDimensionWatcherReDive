using System.Text.Json;
using System.Threading.Channels;
using Microsoft.Extensions.Caching.Distributed;
using SecondDimensionWatcherReDive.Controllers;
using SecondDimensionWatcherReDive.Data;

namespace SecondDimensionWatcherReDive.Services;

public class UpdateDownloadStatusBackgroundService : BackgroundService
{
    private readonly Channel<FileDownloadStatus> _fileDownloadStatus;
    private readonly IDistributedCache _distributedCache;

    public UpdateDownloadStatusBackgroundService(Channel<FileDownloadStatus> fileDownloadStatus, IDistributedCache distributedCache)
    {
        _fileDownloadStatus = fileDownloadStatus;
        _distributedCache = distributedCache;
    }

    protected override async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        var reader = _fileDownloadStatus.Reader;
        while (!cancellationToken.IsCancellationRequested)
        {
            var status = await reader.ReadAsync(cancellationToken);
            var externalStatus = status.ToExternal();
            var key = status.ItemId.ToString();
            var value = JsonSerializer.Serialize(externalStatus, SecondDimensionWatcherReDive.Controllers.External.AppJsonSerializerContext.Default.FileDownloadStatus);
            if (status.State == FileDownloadState.Finished)
                await _distributedCache.SetStringAsync(key, value,
                    new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5) },
                    cancellationToken);
            else
                await _distributedCache.SetStringAsync(key, value, cancellationToken);
        }
    }
}
