namespace SecondDimensionWatcherReDive.Framework.DataRepository;

public enum MediaLibrarySourceRemoveResult
{
    Removed,
    NotFound,
    Busy
}

public sealed record MediaLibrarySource(
    Guid Id,
    string Path,
    bool IsMonitoring,
    DateTimeOffset CreatedAt,
    DateTimeOffset? LastScanAt,
    string? LastError,
    int LastImportedCount,
    int LastUpdatedCount,
    int LastRemovedCount,
    int LastSkippedCount);
