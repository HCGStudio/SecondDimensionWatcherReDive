using SecondDimensionWatcherReDive.Framework.DataRepository;
using SecondDimensionWatcherReDive.Framework.FileStore;
using SecondDimensionWatcherReDive.NFS.Protocol;

namespace SecondDimensionWatcherReDive.NFS.Vfs;

internal sealed record NfsResolvedNode(
    NfsHandleKind Kind,
    string VirtualPath,
    long Size,
    DateTimeOffset MTime,
    string? FileStoreName = null,
    string? PhysicalPath = null);

internal sealed record NfsDirectoryChild(
    string Name,
    NfsHandleKind Kind,
    string VirtualPath,
    long Size,
    DateTimeOffset MTime);

internal sealed class NfsVfsAdapter(
    IFileExplorer explorer,
    IFileMappingRepository mappingRepository,
    IFileStoreProvider storeProvider)
{
    public async Task<NfsResolvedNode?> ResolveAsync(NfsFileHandle handle, CancellationToken cancellationToken)
    {
        if (handle.Kind == NfsHandleKind.Root)
            return new NfsResolvedNode(NfsHandleKind.Root, "/", 0, DateTimeOffset.UnixEpoch);

        var entry = await mappingRepository.FindFileSystemEntryAsync(
            handle.VirtualPath,
            cancellationToken);
        if (entry is null)
            return null;

        if (!entry.IsDirectory && entry.Mapping is { } mapping)
        {
            var store = storeProvider.GetClient(mapping.FileStore);
            if (store is null)
                return null;

            var info = await store.FileInfoAsync(mapping.PhysicalPath, cancellationToken);
            return new NfsResolvedNode(
                NfsHandleKind.File,
                handle.VirtualPath,
                info.Length ?? 0,
                info.LastModifiedUtc ?? DateTimeOffset.UnixEpoch,
                mapping.FileStore,
                mapping.PhysicalPath);
        }

        return new NfsResolvedNode(NfsHandleKind.Directory, handle.VirtualPath, 0, DateTimeOffset.UnixEpoch);
    }

    public async Task<NfsResolvedNode?> LookupAsync(
        string parentVirtualPath,
        string name,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(name) || name.Contains('/'))
            return null;

        var parent = parentVirtualPath == "/" ? string.Empty : parentVirtualPath.TrimEnd('/');
        var childPath = parent + "/" + name;
        var entry = await mappingRepository.FindFileSystemEntryAsync(childPath, cancellationToken);
        if (entry is null)
            return null;
        return await ResolveAsync(
            new NfsFileHandle(
                entry.IsDirectory ? NfsHandleKind.Directory : NfsHandleKind.File,
                childPath),
            cancellationToken);
    }

    public async Task<IReadOnlyList<NfsDirectoryChild>> ListAsync(
        string virtualPath,
        CancellationToken cancellationToken)
    {
        var token = new DirectoryToken(virtualPath, NameFromPath(virtualPath));
        var raw = await explorer.GetDirectoryEntriesAsync(token, cancellationToken);

        var result = new List<NfsDirectoryChild>(raw.Count);
        foreach (var entry in raw)
        {
            result.Add(new NfsDirectoryChild(
                entry.FileName,
                entry.IsDirectory ? NfsHandleKind.Directory : NfsHandleKind.File,
                entry.Path,
                entry.FileInfo?.Length ?? 0,
                entry.FileInfo?.LastModifiedUtc ?? DateTimeOffset.UnixEpoch));
        }

        result.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));
        return result;
    }

    public Task<Stream> OpenReadAsync(NfsFileHandle handle, CancellationToken cancellationToken)
    {
        if (handle.Kind != NfsHandleKind.File)
            throw new InvalidOperationException("OpenReadAsync called on a non-file handle");
        var fileName = NameFromPath(handle.VirtualPath);
        return explorer.OpenReadStreamAsync(new FileToken(handle.VirtualPath, fileName), cancellationToken);
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
