namespace SecondDimensionWatcherReDive.Controllers.External;

internal sealed record MediaLibrarySourceResponse(
    Guid Id,
    string Path,
    bool IsMonitoring,
    DateTimeOffset CreatedAt,
    DateTimeOffset? LastScanAt,
    string? LastError,
    int LastImportedCount,
    int LastUpdatedCount,
    int LastRemovedCount,
    int LastSkippedCount,
    bool IsScanning);

internal sealed record CreateMediaLibrarySourceRequest(string Path, bool IsMonitoring);

internal sealed record UpdateMediaLibrarySourceRequest(bool IsMonitoring);

internal sealed record QueueMediaLibraryScanResponse(bool Queued);
