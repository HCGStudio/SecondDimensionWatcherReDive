namespace SecondDimensionWatcherReDive.Framework.DataRepository;

/// <summary>
/// A materialized node in the virtual file-system hierarchy. Directory nodes
/// have no mapping; file nodes carry the mapping needed to open or stat them.
/// </summary>
public sealed record FileSystemEntry(
    string Path,
    string ParentPath,
    string Name,
    bool IsDirectory,
    FileMapping? Mapping);
