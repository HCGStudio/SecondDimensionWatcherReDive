namespace SecondDimensionWatcherReDive.Services;

internal sealed class TmdbImageProxyOptions
{
    public const string SectionName = "TmdbImageProxy";

    public const long MaximumCacheSizeBytes = 512L * 1024 * 1024;

    public const int MaximumImageSizeBytes = 20 * 1024 * 1024;

    public const int MaximumConcurrentFetchCount = 8;

    public const int MaximumPendingFetchCount = 128;

    public const int MaximumRequestsPerMinute = 5_000;

    public static readonly TimeSpan MaximumCacheDuration = TimeSpan.FromDays(30);

    public static readonly TimeSpan MaximumClientCacheDuration = TimeSpan.FromDays(7);

    public long CacheSizeBytes { get; set; } = 64L * 1024 * 1024;

    public int MaxImageBytes { get; set; } = 5 * 1024 * 1024;

    public int MaxConcurrentFetches { get; set; } = 4;

    public int MaxPendingFetches { get; set; } = 32;

    public int RequestsPerMinute { get; set; } = 240;

    public TimeSpan CacheDuration { get; set; } = TimeSpan.FromDays(7);

    public TimeSpan ClientCacheDuration { get; set; } = TimeSpan.FromDays(1);
}
