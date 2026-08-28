namespace SecondDimensionWatcherReDive.Models;

public class MediaLibrarySource
{
    public Guid Id { get; set; }

    public string Path { get; set; } = string.Empty;

    public bool IsMonitoring { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? LastScanAt { get; set; }

    public string? LastError { get; set; }

    public int LastImportedCount { get; set; }

    public int LastUpdatedCount { get; set; }

    public int LastRemovedCount { get; set; }

    public int LastSkippedCount { get; set; }
}
