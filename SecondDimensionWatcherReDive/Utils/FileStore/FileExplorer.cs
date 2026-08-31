using SecondDimensionWatcherReDive.Framework.DataRepository;
using SecondDimensionWatcherReDive.Framework.FileStore;

namespace SecondDimensionWatcherReDive.Utils.FileStore;

public class FileExplorer(
    IFileMappingRepository fileMappingRepository,
    IFileStoreProvider fileStoreProvider) : IFileExplorer
{
    private const int DirectoryPageSize = 256;
    private const int StableEnumerationAttempts = 3;

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

    public async Task<FileExplorePage?> GetDirectoryEntriesPageAsync(
        DirectoryToken token,
        long? afterCookie,
        int take,
        CancellationToken cancellationToken)
    {
        var parentPath = NormalizeParentPath(token.Path);
        var nodePage = await fileMappingRepository.GetImmediateChildrenPageAsync(
            parentPath,
            afterCookie,
            take,
            cancellationToken);
        if (nodePage is null) return null;
        if (!nodePage.CursorIsValid)
        {
            return new FileExplorePage(
                [],
                nodePage.Generation,
                null,
                false);
        }

        var infoByPath = await StatFilesAsync(nodePage.Items, cancellationToken);
        var entries = nodePage.Items.Select(node => new FileExploreEntry(
            node.Path,
            node.Name,
            node.IsDirectory,
            node.Mapping,
            node.Mapping is not null
                && infoByPath.TryGetValue(node.Mapping.PhysicalPath, out var info)
                    ? info
                    : null,
            node.EntryId,
            node.Cookie)).ToList();
        return new FileExplorePage(
            entries,
            nodePage.Generation,
            nodePage.NextCookie,
            true);
    }

    public async Task<IReadOnlyList<FileExploreEntry>> GetDirectoryEntriesAsync(
        DirectoryToken token,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < StableEnumerationAttempts; attempt++)
        {
            var entries = new List<FileExploreEntry>();
            long? cookie = null;
            long? generation = null;
            var restart = false;
            do
            {
                var page = await GetDirectoryEntriesPageAsync(
                    token,
                    cookie,
                    DirectoryPageSize,
                    cancellationToken);
                if (page is null) return [];
                if (!page.CursorIsValid
                    || (generation.HasValue && generation.Value != page.Generation))
                {
                    restart = true;
                    break;
                }

                generation ??= page.Generation;
                entries.AddRange(page.Items);
                cookie = page.NextCookie;
            } while (cookie.HasValue);

            if (!restart)
            {
                return entries
                    .OrderByDescending(entry => entry.IsDirectory)
                    .ThenBy(entry => entry.FileName, StringComparer.Ordinal)
                    .ToList();
            }
        }

        throw new InvalidOperationException(
            "The directory changed continuously while its physical metadata was being read.");
    }

    public async Task<Stream> OpenReadStreamAsync(FileToken token, CancellationToken cancellationToken)
    {
        var mapping = await fileMappingRepository.FindByVirtualPathAsync(token.Path, cancellationToken)
                      ?? throw new FileNotFoundException($"No mapping for virtual path '{token.Path}'.");
        var store = fileStoreProvider.GetRequiredClient(mapping.FileStore);
        return await store.OpenReadStreamAsync(mapping.PhysicalPath, cancellationToken);
    }

    private async Task<IReadOnlyDictionary<string, FileStoreInfo>> StatFilesAsync(
        IReadOnlyList<FileSystemEntry> nodes,
        CancellationToken cancellationToken)
    {
        var infoByPath = new Dictionary<string, FileStoreInfo>(StringComparer.Ordinal);
        foreach (var group in nodes
                     .Where(node => !node.IsDirectory && node.Mapping is not null)
                     .GroupBy(node => node.Mapping!.FileStore, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var store = fileStoreProvider.GetClient(group.Key);
                if (store is null) continue;
                var batch = await store.GetFileInfosAsync(
                    group.Select(node => node.Mapping!.PhysicalPath).ToArray(),
                    cancellationToken);
                foreach (var pair in batch)
                    infoByPath[pair.Key] = pair.Value;
            }
            catch (Exception) when (!cancellationToken.IsCancellationRequested)
            {
                // A removed provider or stale physical batch leaves stat fields null,
                // while the durable virtual directory remains fully enumerable.
            }
        }

        return infoByPath;
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
