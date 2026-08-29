namespace SecondDimensionWatcherReDive.Services;

internal sealed class TmdbImageProxyOptions
{
    public const string SectionName = "TmdbImageProxy";

    public long CacheSizeBytes { get; set; } = 64L * 1024 * 1024;

    public int MaxImageBytes { get; set; } = 5 * 1024 * 1024;

    public TimeSpan CacheDuration { get; set; } = TimeSpan.FromDays(7);

    public TimeSpan ClientCacheDuration { get; set; } = TimeSpan.FromDays(1);
}
