using SecondDimensionWatcherReDive.Framework.DataRepository;
using SecondDimensionWatcherReDive.Framework.FileStore;

namespace SecondDimensionWatcherReDive.Utils.FileStore;

public class FileExplorer(
    IFileMappingRepository fileMappingRepository,
    IFileStoreProvider fileStoreProvider) : IFileExplorer
{
    public async Task<IReadOnlyList<IFileExploreToken>> EnumerateDirectoryAsync(
        DirectoryToken token,
        CancellationToken cancellationToken)
    {
        var parentPath = NormalizeParentPath(token.Path);
        var nodes = await fileMappingRepository.GetImmediateChildrenAsync(
            parentPath,
            cancellationToken);
        return nodes.Select<FileSystemEntry, IFileExploreToken>(node => node.IsDirectory
            ? new DirectoryToken(node.Path, node.Name)
            : new FileToken(node.Path, node.Name)).ToList();
    }

    public async Task<IReadOnlyList<FileExploreEntry>> GetDirectoryEntriesAsync(
        DirectoryToken token,
        CancellationToken cancellationToken)
    {
        var parentPath = NormalizeParentPath(token.Path);

        var nodes = await fileMappingRepository.GetImmediateChildrenAsync(
            parentPath,
            cancellationToken);
        var infoByPath = new Dictionary<string, FileStoreInfo>(StringComparer.Ordinal);

        var statTasks = nodes
            .Where(node => !node.IsDirectory && node.Mapping is not null)
            .GroupBy(node => node.Mapping!.FileStore, StringComparer.Ordinal)
            .Select(async group =>
            {
                var store = fileStoreProvider.GetRequiredClient(group.Key);
                try
                {
                    return await store.GetFileInfosAsync(
                        group.Select(node => node.Mapping!.PhysicalPath).ToArray(),
                        cancellationToken);
                }
                catch (Exception) when (!cancellationToken.IsCancellationRequested)
                {
                    // A stale physical file must not make the virtual directory
                    // unreadable. Callers expose null stat fields for this batch.
                    return (IReadOnlyDictionary<string, FileStoreInfo>)
                        new Dictionary<string, FileStoreInfo>(StringComparer.Ordinal);
                }
            })
            .ToArray();

        if (statTasks.Length > 0)
        {
            var batches = await Task.WhenAll(statTasks);
            foreach (var batch in batches)
                foreach (var pair in batch)
                    infoByPath[pair.Key] = pair.Value;
        }

        return nodes.Select(node => new FileExploreEntry(
            node.Path,
            node.Name,
            node.IsDirectory,
            node.Mapping,
            node.Mapping is not null
                && infoByPath.TryGetValue(node.Mapping.PhysicalPath, out var info)
                    ? info
                    : null)).ToList();
    }

    public async Task<Stream> OpenReadStreamAsync(FileToken token, CancellationToken cancellationToken)
    {
        var mapping = await fileMappingRepository.FindByVirtualPathAsync(token.Path, cancellationToken)
                      ?? throw new FileNotFoundException($"No mapping for virtual path '{token.Path}'.");
        var store = fileStoreProvider.GetRequiredClient(mapping.FileStore);
        return await store.OpenReadStreamAsync(mapping.PhysicalPath, cancellationToken);
    }

    private static string NormalizeDirectory(string path)
    {
        if (string.IsNullOrEmpty(path)) return "/";
        return path.EndsWith('/') ? path : path + "/";
    }

    private static string NormalizeParentPath(string path)
    {
        var parentPath = NormalizeDirectory(path).TrimEnd('/');
        return parentPath.Length == 0 ? "/" : parentPath;
    }
}
