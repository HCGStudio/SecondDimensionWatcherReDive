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
        var prefix = NormalizeDirectory(token.Path);

        if (prefix == "/")
        {
            var roots = await fileMappingRepository.GetRootEntriesAsync(cancellationToken);
            return roots
                .Select<RootEntry, IFileExploreToken>(r => r.IsDirectory
                    ? new DirectoryToken("/" + r.Name, r.Name)
                    : new FileToken("/" + r.Name, r.Name))
                .ToList();
        }

        var mappings = await fileMappingRepository.GetByVirtualPathPrefixAsync(prefix, cancellationToken);

        var results = new List<IFileExploreToken>();
        var seenDirectories = new HashSet<string>(StringComparer.Ordinal);

        foreach (var mapping in mappings)
        {
            if (!mapping.VirtualPath.StartsWith(prefix, StringComparison.Ordinal)) continue;
            var remainder = mapping.VirtualPath[prefix.Length..];
            if (remainder.Length == 0) continue;

            var slashIndex = remainder.IndexOf('/');
            if (slashIndex < 0)
            {
                results.Add(new FileToken(mapping.VirtualPath, remainder));
            }
            else
            {
                var dirName = remainder[..slashIndex];
                var dirPath = prefix + dirName;
                if (seenDirectories.Add(dirPath))
                    results.Add(new DirectoryToken(dirPath, dirName));
            }
        }

        return results;
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
}
