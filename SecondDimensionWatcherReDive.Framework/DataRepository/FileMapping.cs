namespace SecondDimensionWatcherReDive.Framework.DataRepository;

public sealed record FileMapping(
    Guid Id,
    Guid AnimationInfoId,
    string VirtualPath,
    string PhysicalPath,
    string FileStore);
