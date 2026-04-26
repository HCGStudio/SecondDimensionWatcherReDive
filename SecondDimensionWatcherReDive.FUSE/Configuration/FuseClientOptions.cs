namespace SecondDimensionWatcherReDive.FUSE.Configuration;

internal sealed record FuseClientOptions(
    Uri ServerUrl,
    string Username,
    string Password,
    string MountPoint,
    TimeSpan CacheTtl,
    bool AllowOther,
    bool Foreground,
    bool DebugFuse,
    string UserAgent);
