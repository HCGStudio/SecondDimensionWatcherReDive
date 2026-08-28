namespace SecondDimensionWatcherReDive.Services;

public sealed class MediaLibraryOptions
{
    public const string SectionName = "MediaLibrary";

    public TimeSpan ScanInterval { get; set; } = TimeSpan.FromMinutes(5);

    public TimeSpan SettlingPeriod { get; set; } = TimeSpan.FromSeconds(30);

    public TimeSpan MissingGracePeriod { get; set; } = TimeSpan.FromHours(24);

    public string[] AllowedRoots { get; set; } = [];

    public string? DownloadRoot { get; set; }
}
