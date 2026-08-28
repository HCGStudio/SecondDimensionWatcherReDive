namespace SecondDimensionWatcherReDive.Framework.DataRepository;

public interface IMediaLibraryScanLease : IAsyncDisposable
{
}

public interface IMediaLibrarySourceRepository
{
    Task<IReadOnlyList<MediaLibrarySource>> GetAllAsync(CancellationToken cancellationToken);

    Task<MediaLibrarySource?> FindByIdAsync(Guid id, CancellationToken cancellationToken);

    Task<MediaLibrarySource?> FindByPathAsync(string path, CancellationToken cancellationToken);

    Task<IMediaLibraryScanLease?> TryAcquireScanLeaseAsync(
        Guid sourceId,
        CancellationToken cancellationToken);

    Task<bool> TryAddAsync(MediaLibrarySource source, CancellationToken cancellationToken);

    Task<bool> SetMonitoringAsync(
        Guid id,
        bool isMonitoring,
        CancellationToken cancellationToken);

    Task<bool> UpdateScanResultAsync(
        Guid id,
        DateTimeOffset scannedAt,
        string? error,
        int importedCount,
        int updatedCount,
        int removedCount,
        int skippedCount,
        CancellationToken cancellationToken);

    Task<MediaLibrarySourceRemoveResult> TryRemoveByIdAsync(
        Guid id,
        CancellationToken cancellationToken);
}
