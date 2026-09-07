using Microsoft.EntityFrameworkCore;
using SecondDimensionWatcherReDive.Framework.DataRepository;
using SecondDimensionWatcherReDive.Utils.FileDownload;

namespace SecondDimensionWatcherReDive.Repositories;

public sealed class TranscodeCapacityRepository([FromKeyedServices("capacity")] Models.ApplicationContext context) : ITranscodeCapacityRepository
{
    public async Task<IReadOnlyList<TranscodeCapacityReservation>> ListActiveAsync(CancellationToken cancellationToken) =>
        (await context.Set<Models.TranscodeCapacityReservation>()
            .FromSqlRaw("SELECT * FROM \"TranscodeCapacityReservations\" WHERE \"LeaseUntil\" > clock_timestamp()")
            .AsNoTracking().ToListAsync(cancellationToken))
            .Select(row => new TranscodeCapacityReservation(row.Id, CapacityVolume.NormalizeDirectoryIdentity(row.DirectoryPath),
                row.VolumeIdentity, row.CountsAgainstDownloads, row.BudgetBytes, row.WrittenBytes, row.LeaseUntil)).ToList();

    public async Task AddAsync(Guid id, string directoryPath, string? volumeIdentity, bool countsAgainstDownloads, long budgetBytes,
        int leaseSeconds, CancellationToken cancellationToken) =>
        await context.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "TranscodeCapacityReservations"
                ("Id", "DirectoryPath", "VolumeIdentity", "CountsAgainstDownloads", "BudgetBytes", "WrittenBytes", "LeaseUntil")
            VALUES ({id}, {CapacityVolume.NormalizeDirectoryIdentity(directoryPath)}, {volumeIdentity}, {countsAgainstDownloads}, {budgetBytes}, 0,
                clock_timestamp() + make_interval(secs => {leaseSeconds}))
            """, cancellationToken);

    public async Task<bool> RenewAsync(Guid id, long writtenBytes, int leaseSeconds, CancellationToken cancellationToken) =>
        await context.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE "TranscodeCapacityReservations"
            SET "WrittenBytes" = {writtenBytes}, "LeaseUntil" = clock_timestamp() + make_interval(secs => {leaseSeconds})
            WHERE "Id" = {id} AND "LeaseUntil" > clock_timestamp()
            """, cancellationToken) == 1;

    public async Task<bool> ExtendLeaseAsync(Guid id, int leaseSeconds, CancellationToken cancellationToken) =>
        await context.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE "TranscodeCapacityReservations"
            SET "LeaseUntil" = clock_timestamp() + make_interval(secs => {leaseSeconds})
            WHERE "Id" = {id} AND "LeaseUntil" > clock_timestamp()
            """, cancellationToken) == 1;

    public async Task RemoveAsync(Guid id, CancellationToken cancellationToken) =>
        await context.Set<Models.TranscodeCapacityReservation>().Where(row => row.Id == id)
            .ExecuteDeleteAsync(cancellationToken);

    public async Task PruneExpiredAsync(CancellationToken cancellationToken)
    {
        await context.Database.ExecuteSqlRawAsync(
            "DELETE FROM \"TranscodeCapacityReservations\" WHERE \"LeaseUntil\" <= clock_timestamp()", cancellationToken);
        await context.Database.ExecuteSqlRawAsync(
            "DELETE FROM \"TranscodeCacheReaders\" WHERE \"LeaseUntil\" <= clock_timestamp()", cancellationToken);
    }

    public async Task<IReadOnlyList<TranscodeCacheReader>> ListActiveReadersAsync(CancellationToken cancellationToken) =>
        (await context.Set<Models.TranscodeCacheReader>()
            .FromSqlRaw("SELECT * FROM \"TranscodeCacheReaders\" WHERE \"LeaseUntil\" > clock_timestamp()")
            .AsNoTracking().ToListAsync(cancellationToken))
            .Select(row => new TranscodeCacheReader(row.Id, CapacityVolume.NormalizeDirectoryIdentity(row.DirectoryPath)))
            .ToList();

    public async Task RegisterReaderAsync(Guid id, string directoryPath, int leaseSeconds, CancellationToken cancellationToken) =>
        await context.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "TranscodeCacheReaders" ("Id", "DirectoryPath", "LeaseUntil")
            VALUES ({id}, {CapacityVolume.NormalizeDirectoryIdentity(directoryPath)}, clock_timestamp() + make_interval(secs => {leaseSeconds}))
            ON CONFLICT ("Id") DO UPDATE SET "DirectoryPath" = EXCLUDED."DirectoryPath", "LeaseUntil" = EXCLUDED."LeaseUntil"
            """, cancellationToken);

    public async Task<bool> RenewReaderAsync(Guid id, int leaseSeconds, CancellationToken cancellationToken) =>
        await context.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE "TranscodeCacheReaders"
            SET "LeaseUntil" = clock_timestamp() + make_interval(secs => {leaseSeconds})
            WHERE "Id" = {id} AND "LeaseUntil" > clock_timestamp()
            """, cancellationToken) == 1;

    public async Task RemoveReaderAsync(Guid id, CancellationToken cancellationToken) =>
        await context.Set<Models.TranscodeCacheReader>().Where(row => row.Id == id).ExecuteDeleteAsync(cancellationToken);
}
