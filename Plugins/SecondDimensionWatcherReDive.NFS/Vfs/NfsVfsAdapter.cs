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

        if (handle.Kind == NfsHandleKind.File)
        {
            var mapping = await mappingRepository.FindByVirtualPathAsync(handle.VirtualPath, cancellationToken);
            if (mapping is null)
                return null;

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

        // Directory: probe by enumerating; a dir exists if it has any children
        // (or it is the root, handled above).
        var children = await ListAsync(handle.VirtualPath, cancellationToken);
        if (children.Count == 0)
            return null;
        return new NfsResolvedNode(NfsHandleKind.Directory, handle.VirtualPath, 0, DateTimeOffset.UnixEpoch);
    }

    public async Task<NfsResolvedNode?> LookupAsync(
        string parentVirtualPath,
        string name,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(name) || name.Contains('/'))
            return null;

        var children = await ListAsync(parentVirtualPath, cancellationToken);
        var match = children.FirstOrDefault(c => string.Equals(c.Name, name, StringComparison.Ordinal));
        if (match is null)
            return null;

        if (match.Kind == NfsHandleKind.File)
        {
            var mapping = await mappingRepository.FindByVirtualPathAsync(match.VirtualPath, cancellationToken);
            if (mapping is null)
                return null;
            var store = storeProvider.GetClient(mapping.FileStore);
            if (store is null)
                return null;
            var info = await store.FileInfoAsync(mapping.PhysicalPath, cancellationToken);
            return new NfsResolvedNode(
                NfsHandleKind.File,
                match.VirtualPath,
                info.Length ?? 0,
                info.LastModifiedUtc ?? DateTimeOffset.UnixEpoch,
                mapping.FileStore,
                mapping.PhysicalPath);
        }

        return new NfsResolvedNode(NfsHandleKind.Directory, match.VirtualPath, 0, DateTimeOffset.UnixEpoch);
    }

    public async Task<IReadOnlyList<NfsDirectoryChild>> ListAsync(
        string virtualPath,
        CancellationToken cancellationToken)
    {
        var token = new DirectoryToken(virtualPath, NameFromPath(virtualPath));
        var raw = await explorer.EnumerateDirectoryAsync(token, cancellationToken);

        var result = new List<NfsDirectoryChild>(raw.Count);
        foreach (var entry in raw)
        {
            switch (entry)
            {
                case FileToken file:
                {
                    var mapping = await mappingRepository.FindByVirtualPathAsync(file.Path, cancellationToken);
                    long size = 0;
                    var mtime = DateTimeOffset.UnixEpoch;
                    if (mapping is not null)
                    {
                        var store = storeProvider.GetClient(mapping.FileStore);
                        if (store is not null)
                        {
                            var info = await store.FileInfoAsync(mapping.PhysicalPath, cancellationToken);
                            size = info.Length ?? 0;
                            mtime = info.LastModifiedUtc ?? DateTimeOffset.UnixEpoch;
                        }
                    }
                    result.Add(new NfsDirectoryChild(file.FileName, NfsHandleKind.File, file.Path, size, mtime));
                    break;
                }
                case DirectoryToken dir:
                    result.Add(new NfsDirectoryChild(dir.FileName, NfsHandleKind.Directory, dir.Path, 0, DateTimeOffset.UnixEpoch));
                    break;
            }
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
