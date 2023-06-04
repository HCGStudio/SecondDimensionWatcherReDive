using System.Threading.Channels;
using Microsoft.EntityFrameworkCore;
using SecondDimensionWatcherReDive.Data;
using SecondDimensionWatcherReDive.Models;

namespace SecondDimensionWatcherReDive.Services;

public class CompleteDownload : BackgroundService
{
    private readonly Channel<DownloadCompleteRequest> _downloadCompleteRequest;
    private readonly IServiceScopeFactory _scopeFactory;

    public CompleteDownload(Channel<DownloadCompleteRequest> downloadCompleteRequest, IServiceScopeFactory scopeFactory)
    {
        _downloadCompleteRequest = downloadCompleteRequest;
        _scopeFactory = scopeFactory;
    }

    protected override async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        await using var applicationContext = scope.ServiceProvider.GetRequiredService<ApplicationContext>();
        var reader = _downloadCompleteRequest.Reader;
        while (await reader.WaitToReadAsync(cancellationToken))
        {
            var request = await reader.ReadAsync(cancellationToken);

            await using var transaction = await applicationContext.Database.BeginTransactionAsync(cancellationToken);

            var info = await applicationContext.AnimationInfo.FirstOrDefaultAsync(a => a.Id == request.ItemId,
                cancellationToken);
            if (info is null)
            {
                await transaction.RollbackAsync(cancellationToken);
                continue;
            }

            info.IsDownloadFinished = true;
            info.FileStore = request.FileStore;
            info.StorePath = request.StorePath;

            await applicationContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
    }
}