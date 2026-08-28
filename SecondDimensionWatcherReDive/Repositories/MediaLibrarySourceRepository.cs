using System.Buffers.Binary;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using SecondDimensionWatcherReDive.Framework.DataRepository;
using SecondDimensionWatcherReDive.Utils.FileStore;
using MediaLibrarySourceEntity = SecondDimensionWatcherReDive.Models.MediaLibrarySource;

namespace SecondDimensionWatcherReDive.Repositories;

public sealed class MediaLibrarySourceRepository(
    Models.ApplicationContext context,
    DbContextOptions<Models.ApplicationContext> contextOptions)
    : IMediaLibrarySourceRepository
{
    private DbSet<MediaLibrarySourceEntity> Sources => context.Set<MediaLibrarySourceEntity>();

    public async Task<IReadOnlyList<MediaLibrarySource>> GetAllAsync(
        CancellationToken cancellationToken)
    {
        var entities = await Sources
            .AsNoTracking()
            .OrderBy(source => source.Path)
            .ToListAsync(cancellationToken);
        return entities.Select(ToRecord).ToList();
    }

    public async Task<MediaLibrarySource?> FindByIdAsync(
        Guid id,
        CancellationToken cancellationToken)
    {
        var entity = await Sources
            .AsNoTracking()
            .FirstOrDefaultAsync(source => source.Id == id, cancellationToken);
        return entity is null ? null : ToRecord(entity);
    }

    public async Task<MediaLibrarySource?> FindByPathAsync(
        string path,
        CancellationToken cancellationToken)
    {
        var entity = await Sources
            .AsNoTracking()
            .FirstOrDefaultAsync(source => source.Path == path, cancellationToken);
        return entity is null ? null : ToRecord(entity);
    }

    public async Task<IMediaLibraryScanLease?> TryAcquireScanLeaseAsync(
        Guid sourceId,
        CancellationToken cancellationToken)
    {
        var connectionString = context.Database.GetConnectionString()
                               ?? throw new InvalidOperationException(
                                   "A database connection string is required for media library scans.");
        // Session advisory locks must stay on one physical PostgreSQL session.
        // A deployment may enable Npgsql multiplexing globally, so explicitly
        // disable it for this dedicated lease connection.
        var leaseConnectionString = new NpgsqlConnectionStringBuilder(connectionString)
        {
            Multiplexing = false
        }.ConnectionString;
        var connection = new NpgsqlConnection(leaseConnectionString);
        try
        {
            await connection.OpenAsync(cancellationToken);
            var key = CreateScanLockKey(sourceId);
            await using var command = new NpgsqlCommand(
                "SELECT pg_try_advisory_lock(@key)",
                connection);
            command.Parameters.AddWithValue("key", key);
            var acquired = await command.ExecuteScalarAsync(cancellationToken) is true;
            if (!acquired)
            {
                await connection.DisposeAsync();
                return null;
            }

            return new PostgreSqlMediaLibraryScanLease(connection, key);
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }

    public async Task<bool> TryAddAsync(
        MediaLibrarySource source,
        CancellationToken cancellationToken)
    {
        const long mediaLibrarySourceLockKey = 0x5344574D4C494231;
        var strategy = context.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var writeContext = new Models.ApplicationContext(contextOptions);
            await using var transaction = await writeContext.Database
                .BeginTransactionAsync(cancellationToken);
            await writeContext.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT pg_advisory_xact_lock({mediaLibrarySourceLockKey})",
                cancellationToken);

            var existingPaths = await writeContext.Set<MediaLibrarySourceEntity>()
                .AsNoTracking()
                .Select(existing => existing.Path)
                .ToListAsync(cancellationToken);
            if (existingPaths.Any(existing => MediaLibraryPath.PathsOverlap(existing, source.Path)))
                return false;

            await writeContext.Set<MediaLibrarySourceEntity>()
                .AddAsync(ToEntity(source), cancellationToken);
            await writeContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return true;
        });
    }

    public async Task<bool> SetMonitoringAsync(
        Guid id,
        bool isMonitoring,
        CancellationToken cancellationToken)
    {
        var affected = await Sources
            .Where(source => source.Id == id)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(source => source.IsMonitoring, isMonitoring),
                cancellationToken);
        return affected != 0;
    }

    public async Task<bool> UpdateScanResultAsync(
        Guid id,
        DateTimeOffset scannedAt,
        string? error,
        int importedCount,
        int updatedCount,
        int removedCount,
        int skippedCount,
        CancellationToken cancellationToken)
    {
        var affected = await Sources
            .Where(source => source.Id == id)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(source => source.LastScanAt, scannedAt)
                    .SetProperty(source => source.LastError, error)
                    .SetProperty(source => source.LastImportedCount, importedCount)
                    .SetProperty(source => source.LastUpdatedCount, updatedCount)
                    .SetProperty(source => source.LastRemovedCount, removedCount)
                    .SetProperty(source => source.LastSkippedCount, skippedCount),
                cancellationToken);
        return affected != 0;
    }

    public async Task<MediaLibrarySourceRemoveResult> TryRemoveByIdAsync(
        Guid id,
        CancellationToken cancellationToken)
    {
        await using var lease = await TryAcquireScanLeaseAsync(id, cancellationToken);
        if (lease is null) return MediaLibrarySourceRemoveResult.Busy;

        var affected = await Sources
            .Where(source => source.Id == id)
            .ExecuteDeleteAsync(cancellationToken);
        return affected != 0
            ? MediaLibrarySourceRemoveResult.Removed
            : MediaLibrarySourceRemoveResult.NotFound;
    }

    private static MediaLibrarySource ToRecord(MediaLibrarySourceEntity entity) => new(
        entity.Id,
        entity.Path,
        entity.IsMonitoring,
        entity.CreatedAt,
        entity.LastScanAt,
        entity.LastError,
        entity.LastImportedCount,
        entity.LastUpdatedCount,
        entity.LastRemovedCount,
        entity.LastSkippedCount);

    private static MediaLibrarySourceEntity ToEntity(MediaLibrarySource source) => new()
    {
        Id = source.Id,
        Path = source.Path,
        IsMonitoring = source.IsMonitoring,
        CreatedAt = source.CreatedAt,
        LastScanAt = source.LastScanAt,
        LastError = source.LastError,
        LastImportedCount = source.LastImportedCount,
        LastUpdatedCount = source.LastUpdatedCount,
        LastRemovedCount = source.LastRemovedCount,
        LastSkippedCount = source.LastSkippedCount
    };

    private static long CreateScanLockKey(Guid sourceId)
    {
        var digest = SHA256.HashData(sourceId.ToByteArray());
        return BinaryPrimitives.ReadInt64LittleEndian(digest)
               ^ 0x5344574D4C530000L;
    }

    private sealed class PostgreSqlMediaLibraryScanLease(
        NpgsqlConnection connection,
        long key) : IMediaLibraryScanLease
    {
        public async ValueTask DisposeAsync()
        {
            try
            {
                await using var command = new NpgsqlCommand(
                    "SELECT pg_advisory_unlock(@key)",
                    connection);
                command.Parameters.AddWithValue("key", key);
                var released = await command.ExecuteScalarAsync() is true;
                if (!released)
                    NpgsqlConnection.ClearPool(connection);
            }
            catch (NpgsqlException)
            {
                // Closing a broken physical session releases its advisory locks.
                // Clear the pool so it cannot be reused with uncertain session state.
                NpgsqlConnection.ClearPool(connection);
            }
            finally
            {
                await connection.DisposeAsync();
            }
        }
    }
}
