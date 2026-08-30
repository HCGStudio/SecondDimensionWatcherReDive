namespace SecondDimensionWatcherReDive.Services.Transcoding;

internal sealed class TranscodingOptions
{
    public const string SectionName = "Transcoding";

    public bool Enabled { get; set; } = true;
    public string CachePath { get; set; } = string.Empty;
    public string FfmpegPath { get; set; } = "ffmpeg";
    public string FfprobePath { get; set; } = "ffprobe";
    public int MaxConcurrentJobs { get; set; } = 1;
    public int QueueCapacity { get; set; } = 8;
    public int MaxThreadsPerJob { get; set; } = 2;
    public long MaxMemoryBytesPerJob { get; set; } = 2L * 1024 * 1024 * 1024;
    public long MaxDiskBytesPerJob { get; set; } = 20L * 1024 * 1024 * 1024;
    public long MaxCacheBytes { get; set; } = 100L * 1024 * 1024 * 1024;
    public TimeSpan JobTimeout { get; set; } = TimeSpan.FromHours(6);
    public TimeSpan CacheTtl { get; set; } = TimeSpan.FromDays(14);
    public TimeSpan CleanupInterval { get; set; } = TimeSpan.FromMinutes(5);
    public TimeSpan SessionTtl { get; set; } = TimeSpan.FromMinutes(15);
    public int SegmentDurationSeconds { get; set; } = 6;
    public int VideoCrf { get; set; } = 23;
    public string VideoPreset { get; set; } = "veryfast";
    public string? HardwareVideoEncoder { get; set; }
    public string[] HardwareInputArguments { get; set; } = [];
    public bool BurnBitmapSubtitles { get; set; }
}
