namespace SecondDimensionWatcherReDive.Framework.DataRepository;

/// <summary>
/// A materialized node in the virtual file-system hierarchy. Directory nodes
/// have no mapping; file nodes carry the mapping needed to open or stat them.
/// </summary>
public sealed record FileSystemEntry(
    Guid EntryId,
    string Path,
    string ParentPath,
    string Name,
    bool IsDirectory,
    int DescendantFileCount,
    long Cookie,
    FileMapping? Mapping)
{
    // Source-compatible adapter for repository fakes and external consumers that
    // predate stable entry identifiers. Production repositories always populate
    // EntryId, count and cookie from the materialized hierarchy.
    public FileSystemEntry(
        string Path,
        string ParentPath,
        string Name,
        bool IsDirectory,
        FileMapping? Mapping)
        : this(Guid.Empty, Path, ParentPath, Name, IsDirectory, 1, 0, Mapping)
    {
    }
}

public sealed record FileSystemDirectoryPage(
    IReadOnlyList<FileSystemEntry> Items,
    long Generation,
    long? NextCookie,
    bool CursorIsValid);

public enum VirtualPathConflictKind
{
    AncestorFile,
    ExistingFile,
    DescendantDirectory,
    ProposedPrefix
}

public sealed record VirtualPathNamespaceConflict(
    string ProposedPath,
    string OccupiedPath,
    VirtualPathConflictKind Kind);
