namespace SecondDimensionWatcherReDive.Controllers.External;

internal sealed record VfsEntry(string Name, bool IsDirectory, long? Size, DateTimeOffset? LastModifiedUtc);
