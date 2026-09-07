using SecondDimensionWatcherReDive.Framework.DataRepository;
using SecondDimensionWatcherReDive.Framework.FileStore;
using SecondDimensionWatcherReDive.NFS.Protocol;

namespace SecondDimensionWatcherReDive.NFS.Vfs;

internal sealed record NfsResolvedNode(
    Guid EntryId,
    NfsHandleKind Kind,
    string VirtualPath,
    long Size,
    DateTimeOffset MTime,
    string? FileStoreName = null,
    string? PhysicalPath = null);

internal sealed record NfsDirectoryChild(
    Guid EntryId,
    long Cookie,
    string Name,
    NfsHandleKind Kind,
    string VirtualPath,
    long Size,
    DateTimeOffset MTime);

internal sealed record NfsDirectoryPage(
    IReadOnlyList<NfsDirectoryChild> Items,
    long Generation,
    bool HasMore,
    bool CursorIsValid);

internal sealed class NfsVfsAdapter(
    IFileExplorer explorer,
    IFileMappingRepository mappingRepository,
    IFileStoreProvider storeProvider)
{
    public async Task<NfsResolvedNode?> ResolveAsync(
        NfsFileHandle handle,
        CancellationToken cancellationToken)
    {
        if (handle.Kind == NfsHandleKind.Root)
        {
            return handle.EntryId == Guid.Empty
                ? new NfsResolvedNode(
                    Guid.Empty,
                    NfsHandleKind.Root,
                    "/",
                    0,
                    DateTimeOffset.UnixEpoch)
                : null;
        }

        var entry = handle.LegacyVirtualPath is { } legacyVirtualPath
            ? await mappingRepository.FindFileSystemEntryAsync(
                legacyVirtualPath,
                cancellationToken)
            : await mappingRepository.FindFileSystemEntryByIdAsync(
                handle.EntryId,
                cancellationToken);
        if (entry is null) return null;

        var actualKind = entry.IsDirectory ? NfsHandleKind.Directory : NfsHandleKind.File;
        if (actualKind != handle.Kind) return null;

        if (!entry.IsDirectory && entry.Mapping is { } mapping)
        {
            FileStoreInfo? info = null;
            try
            {
                var store = storeProvider.GetClient(mapping.FileStore);
                if (store is not null)
                    info = await store.FileInfoAsync(mapping.PhysicalPath, cancellationToken);
            }
            catch (Exception) when (!cancellationToken.IsCancellationRequested)
            {
                // Keep the durable namespace reachable when its backing provider or
                // physical file is temporarily stale. READ will still fail closed.
            }

            return new NfsResolvedNode(
                entry.EntryId,
                NfsHandleKind.File,
                entry.Path,
                info?.Length ?? 0,
                info?.LastModifiedUtc ?? DateTimeOffset.UnixEpoch,
                mapping.FileStore,
                mapping.PhysicalPath);
        }

        return entry.IsDirectory
            ? new NfsResolvedNode(
                entry.EntryId,
                NfsHandleKind.Directory,
                entry.Path,
                0,
                DateTimeOffset.UnixEpoch)
            : null;
    }

    public async Task<NfsResolvedNode?> LookupAsync(
        NfsFileHandle parentHandle,
        string name,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(name) || name.Contains('/')) return null;

        var parent = await ResolveAsync(parentHandle, cancellationToken);
        if (parent is null || parent.Kind == NfsHandleKind.File) return null;
        var parentPath = parent.VirtualPath == "/"
            ? string.Empty
            : parent.VirtualPath.TrimEnd('/');
        var childPath = parentPath + "/" + name;
        var entry = await mappingRepository.FindFileSystemEntryAsync(
            childPath,
            cancellationToken);
        if (entry is null) return null;
        return await ResolveAsync(
            NfsFileHandle.ForStableEntry(
                entry.IsDirectory ? NfsHandleKind.Directory : NfsHandleKind.File,
                entry.EntryId),
            cancellationToken);
    }

    public async Task<NfsResolvedNode?> LookupParentAsync(
        NfsFileHandle handle,
        CancellationToken cancellationToken)
    {
        var current = await ResolveAsync(handle, cancellationToken);
        if (current is null || current.Kind == NfsHandleKind.Root) return null;

        var lastSlash = current.VirtualPath.LastIndexOf('/');
        if (lastSlash <= 0)
            return await ResolveAsync(NfsFileHandle.Root, cancellationToken);

        var parentPath = current.VirtualPath[..lastSlash];
        var parent = await mappingRepository.FindFileSystemEntryAsync(
            parentPath,
            cancellationToken);
        if (parent is null || !parent.IsDirectory) return null;
        return new NfsResolvedNode(
            parent.EntryId,
            NfsHandleKind.Directory,
            parent.Path,
            0,
            DateTimeOffset.UnixEpoch);
    }

    public async Task<NfsDirectoryPage?> ListPageAsync(
        NfsFileHandle directoryHandle,
        long? afterCookie,
        int take,
        CancellationToken cancellationToken)
    {
        var directory = await ResolveAsync(directoryHandle, cancellationToken);
        if (directory is null || directory.Kind == NfsHandleKind.File) return null;

        var token = new DirectoryToken(
            directory.VirtualPath,
            NameFromPath(directory.VirtualPath));
        var page = await explorer.GetDirectoryEntriesPageAsync(
            token,
            afterCookie,
            take,
            cancellationToken);
        if (page is null) return null;

        var items = page.Items.Select(entry => new NfsDirectoryChild(
            entry.EntryId,
            entry.Cookie,
            entry.FileName,
            entry.IsDirectory ? NfsHandleKind.Directory : NfsHandleKind.File,
            entry.Path,
            entry.FileInfo?.Length ?? 0,
            entry.FileInfo?.LastModifiedUtc ?? DateTimeOffset.UnixEpoch)).ToList();
        return new NfsDirectoryPage(
            items,
            page.Generation,
            page.NextCookie.HasValue,
            page.CursorIsValid);
    }

    public async Task<Stream> OpenReadAsync(
        NfsFileHandle handle,
        CancellationToken cancellationToken)
    {
        var resolved = await ResolveAsync(handle, cancellationToken);
        if (resolved is not
            {
                Kind: NfsHandleKind.File,
                FileStoreName: not null,
                PhysicalPath: not null
            })
            throw new FileNotFoundException("The NFS file handle is stale.");

        var store = storeProvider.GetClient(resolved.FileStoreName)
                    ?? throw new IOException(
                        $"The backing file store '{resolved.FileStoreName}' is unavailable.");
        return await store.OpenReadStreamAsync(resolved.PhysicalPath, cancellationToken);
    }

    private static string NameFromPath(string virtualPath)
    {
        var trimmed = virtualPath.EndsWith('/') && virtualPath.Length > 1
            ? virtualPath[..^1]
            : virtualPath;
        var slash = trimmed.LastIndexOf('/');
        return slash < 0 ? trimmed : trimmed[(slash + 1)..];
    }
}
