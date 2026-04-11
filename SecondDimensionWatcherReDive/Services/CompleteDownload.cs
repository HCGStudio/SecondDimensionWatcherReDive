using System.Threading.Channels;
using Microsoft.EntityFrameworkCore;
using SecondDimensionWatcherReDive.Data;
using SecondDimensionWatcherReDive.Framework.FileStore;
using SecondDimensionWatcherReDive.Models;

namespace SecondDimensionWatcherReDive.Services;

public class CompleteDownload(
    Channel<DownloadCompleteRequest> downloadCompleteRequest,
    IServiceScopeFactory scopeFactory,
    ILogger<CompleteDownload> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        var reader = downloadCompleteRequest.Reader;
        while (await reader.WaitToReadAsync(cancellationToken))
        {
            var request = await reader.ReadAsync(cancellationToken);
            await ProcessRequest(request, cancellationToken);
        }
    }

    private async Task ProcessRequest(DownloadCompleteRequest request, CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        await using var applicationContext = scope.ServiceProvider.GetRequiredService<ApplicationContext>();

        await using var transaction = await applicationContext.Database.BeginTransactionAsync(cancellationToken);

        var info = await applicationContext.AnimationInfo
            .Include(a => a.Animation)
            .FirstOrDefaultAsync(a => a.Id == request.ItemId, cancellationToken);

        if (info is null)
        {
            await transaction.RollbackAsync(cancellationToken);
            return;
        }

        info.IsDownloadFinished = true;
        info.DownloadEndTime = DateTimeOffset.Now;
        info.FileStore = request.FileStore;
        info.StorePath = request.StorePath;

        await applicationContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        // Rename files after download completes
        var fileRenamer = scope.ServiceProvider.GetService<IFileRenamer>();
        if (fileRenamer != null && info.Animation != null && info.StorePath != null)
        {
            try
            {
                var context = new FileRenameContext(
                    AnimationName: info.Animation.Name,
                    Season: info.Season ?? 1,
                    Episode: info.Episode,
                    OriginalTitle: info.Title,
                    StorePath: info.StorePath);

                await fileRenamer.RenameAsync(context, cancellationToken);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "File rename failed for {ItemId}", request.ItemId);
            }
        }
    }
}