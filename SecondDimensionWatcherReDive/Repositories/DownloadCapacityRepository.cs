using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using SecondDimensionWatcherReDive.Framework.DataRepository;
using SecondDimensionWatcherReDive.Framework.FileDownload;
using Entry = SecondDimensionWatcherReDive.Models.DownloadCapacityEntry;

namespace SecondDimensionWatcherReDive.Repositories;

public sealed class DownloadCapacityRepository([FromKeyedServices("capacity")] Models.ApplicationContext context) : IDownloadCapacityRepository
{
    public async Task RecoverTrackedAsync(CancellationToken cancellationToken)
    {
        // Rebuild reservations for downloads submitted before this feature (or
        // while it was disabled). Live submission sagas enqueue themselves.
        await context.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "DownloadCapacityEntries"
                ("ItemId", "DownloadAttemptId", "Title", "Hash", "ExpectedBytes", "RemainingBytes", "State", "Paused", "Reason", "CreatedAt", "UpdatedAt")
            SELECT "Id", "DownloadAttemptId", "Title", "AdditionalDownloadInfo", "ReleaseSizeBytes",
                   CASE WHEN "ReleaseSizeBytes" > 0 THEN "ReleaseSizeBytes" ELSE 9223372036854775807 END, 'Submitted', FALSE,
                   'Recovering an existing download reservation; awaiting remote reconciliation.',
                   "DownloadStartTime", clock_timestamp()
            FROM "AnimationInfo"
            WHERE "IsDownloadTracked" AND NOT "IsDownloadFinished" AND "DownloadType" = {FileDownloadTypes.TorrentDownload}
              AND "DownloadCancellationId" IS NULL
              AND ("DownloadSubmissionLeaseId" IS NULL OR "DownloadSubmissionLeaseUntil" <= clock_timestamp())
            ON CONFLICT ("ItemId") DO NOTHING
            """, cancellationToken);
    }

    public async Task<ICapacityTransaction> BeginAsync(CancellationToken cancellationToken)
    {
        var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            await context.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(7364921053)", cancellationToken);
            return new CapacityTransaction(transaction);
        }
        catch
        {
            await transaction.DisposeAsync();
            throw;
        }
    }

    public async Task<IReadOnlyList<DownloadCapacityEntry>> ListAsync(CancellationToken cancellationToken) =>
        await context.Set<Entry>().AsNoTracking().OrderBy(entry => entry.CreatedAt).ThenBy(entry => entry.ItemId)
            .Select(entry => new DownloadCapacityEntry(entry.ItemId, entry.DownloadAttemptId, entry.Title,
                entry.Hash, entry.ExpectedBytes, entry.RemainingBytes, entry.State, entry.Paused, entry.Reason,
                entry.CreatedAt, entry.UpdatedAt)).ToListAsync(cancellationToken);

    public async Task SaveAsync(DownloadCapacityEntry entry, CancellationToken cancellationToken)
    {
        var entity = await context.Set<Entry>().FindAsync([entry.ItemId], cancellationToken);
        if (entity is null)
        {
            entity = new Entry { ItemId = entry.ItemId };
            context.Add(entity);
        }
        entity.DownloadAttemptId = entry.DownloadAttemptId;
        entity.Title = entry.Title;
        entity.Hash = entry.Hash;
        entity.ExpectedBytes = entry.ExpectedBytes;
        entity.RemainingBytes = entry.RemainingBytes;
        entity.State = entry.State;
        entity.Paused = entry.Paused;
        entity.Reason = entry.Reason.Length > 1024 ? entry.Reason[..1024] : entry.Reason;
        entity.CreatedAt = entry.CreatedAt;
        entity.UpdatedAt = entry.UpdatedAt;
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task RemoveAsync(Guid itemId, CancellationToken cancellationToken)
    {
        await context.Set<Entry>().Where(entry => entry.ItemId == itemId).ExecuteDeleteAsync(cancellationToken);
        foreach (var tracked in context.ChangeTracker.Entries<Entry>().Where(entry => entry.Entity.ItemId == itemId))
            tracked.State = EntityState.Detached;
    }

    private sealed class CapacityTransaction(IDbContextTransaction transaction) : ICapacityTransaction
    {
        public Task CommitAsync(CancellationToken cancellationToken) => transaction.CommitAsync(cancellationToken);
        public ValueTask DisposeAsync() => transaction.DisposeAsync();
    }
}
