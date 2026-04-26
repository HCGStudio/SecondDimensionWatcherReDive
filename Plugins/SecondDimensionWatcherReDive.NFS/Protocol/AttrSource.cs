namespace SecondDimensionWatcherReDive.NFS.Protocol;

internal sealed record AttrSource(
    bool IsDirectory,
    long Size,
    DateTimeOffset MTime,
    NfsFileHandle Handle,
    string OwnerName,
    string GroupName,
    int LeaseTimeSeconds);
