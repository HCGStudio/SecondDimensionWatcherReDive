using SecondDimensionWatcherReDive.Framework.DataRepository;
using SecondDimensionWatcherReDive.Framework.FileStore;

namespace SecondDimensionWatcherReDive.IntegrationTest.Helpers;

internal sealed class FakeFileMappingRepository : IFileMappingRepository
{
    private readonly List<FileMapping> _mappings;

    public FakeFileMappingRepository(List<FileMapping> mappings)
    {
        _mappings = mappings;
    }

    public List<string> PrefixCalls { get; } = new();
    public int RootEntriesCalls { get; private set; }
    public List<string> ImmediateChildrenCalls { get; } = new();

    private List<FileMapping> Snapshot() => _mappings.ToList();

    public Task AddRangeAsync(IReadOnlyList<FileMapping> mappings, CancellationToken cancellationToken)
        => throw new NotSupportedException();

    public Task<bool> ReplaceForAnimationInfoAsync(
        Guid animationInfoId,
        long expectedStateVersion,
        string expectedFileStore,
        string expectedStorePath,
        IReadOnlyList<FileMapping> mappings,
        CancellationToken cancellationToken)
    {
        _mappings.RemoveAll(mapping => mapping.AnimationInfoId == animationInfoId);
        _mappings.AddRange(mappings);
        return Task.FromResult(true);
    }

    public Task<IReadOnlyList<FileMapping>> GetForAnimationInfoAsync(
        Guid animationInfoId,
        CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<FileMapping>>(
            Snapshot().Where(mapping => mapping.AnimationInfoId == animationInfoId).ToList());

    public Task<FileMapping?> FindByVirtualPathAsync(string virtualPath, CancellationToken cancellationToken)
        => Task.FromResult(Snapshot().FirstOrDefault(m => m.VirtualPath == virtualPath));

    public Task<FileSystemEntry?> FindFileSystemEntryAsync(
        string virtualPath,
        CancellationToken cancellationToken)
    {
        var normalized = virtualPath == "/" ? "/" : virtualPath.TrimEnd('/');
        var mapping = Snapshot().FirstOrDefault(item => item.VirtualPath == normalized);
        if (mapping is not null)
            return Task.FromResult<FileSystemEntry?>(new FileSystemEntry(
                normalized,
                ParentPath(normalized),
                Path.GetFileName(normalized),
                IsDirectory: false,
                mapping));

        var prefix = normalized + "/";
        return Task.FromResult(Snapshot().Any(item => item.VirtualPath.StartsWith(prefix, StringComparison.Ordinal))
            ? new FileSystemEntry(
                normalized,
                ParentPath(normalized),
                Path.GetFileName(normalized),
                IsDirectory: true,
                Mapping: null)
            : null);
    }

    public Task<IReadOnlyList<FileSystemEntry>> GetImmediateChildrenAsync(
        string parentPath,
        CancellationToken cancellationToken)
    {
        ImmediateChildrenCalls.Add(parentPath);
        var prefix = parentPath == "/" ? "/" : parentPath.TrimEnd('/') + "/";
        var children = new Dictionary<string, FileSystemEntry>(StringComparer.Ordinal);
        foreach (var mapping in Snapshot())
        {
            if (!mapping.VirtualPath.StartsWith(prefix, StringComparison.Ordinal)) continue;
            var remainder = mapping.VirtualPath[prefix.Length..];
            if (remainder.Length == 0) continue;
            var slash = remainder.IndexOf('/');
            var name = slash < 0 ? remainder : remainder[..slash];
            var path = prefix == "/" ? "/" + name : prefix + name;
            children[path] = slash < 0
                ? new FileSystemEntry(path, parentPath, name, IsDirectory: false, mapping)
                : new FileSystemEntry(path, parentPath, name, IsDirectory: true, Mapping: null);
        }

        return Task.FromResult<IReadOnlyList<FileSystemEntry>>(children.Values.ToList());
    }

    public Task<IReadOnlyList<FileMapping>> GetByVirtualPathPrefixAsync(string virtualPathPrefix, CancellationToken cancellationToken)
    {
        PrefixCalls.Add(virtualPathPrefix);
        return Task.FromResult<IReadOnlyList<FileMapping>>(
            Snapshot().Where(m => m.VirtualPath.StartsWith(virtualPathPrefix, StringComparison.Ordinal)).ToList());
    }

    public Task<IReadOnlyList<RootEntry>> GetRootEntriesAsync(CancellationToken cancellationToken)
    {
        RootEntriesCalls++;
        var dict = new Dictionary<string, bool>(StringComparer.Ordinal);
        foreach (var m in Snapshot())
        {
            if (m.VirtualPath.Length <= 1 || !m.VirtualPath.StartsWith('/')) continue;
            var nextSlash = m.VirtualPath.IndexOf('/', 1);
            var name = nextSlash < 0
                ? m.VirtualPath[1..]
                : m.VirtualPath[1..nextSlash];
            var isDir = nextSlash > 0;
            if (!dict.TryGetValue(name, out var existing) || (!existing && isDir))
                dict[name] = isDir;
        }

        return Task.FromResult<IReadOnlyList<RootEntry>>(
            dict.Select(kv => new RootEntry(kv.Key, kv.Value)).ToList());
    }

    public Task<bool> VirtualPathExistsAsync(string virtualPath, CancellationToken cancellationToken)
        => Task.FromResult(Snapshot().Any(m => m.VirtualPath == virtualPath));

    public Task<bool> ExistsForAnimationInfoAsync(Guid animationInfoId, CancellationToken cancellationToken)
        => throw new NotSupportedException();

    public Task<bool> TryFinalizeDownloadCancellationAsync(
        Guid animationInfoId,
        Guid? downloadAttemptId,
        Guid cancellationAttemptId,
        CancellationToken cancellationToken)
    {
        _mappings.RemoveAll(mapping => mapping.AnimationInfoId == animationInfoId);
        return Task.FromResult(true);
    }

    public Task RemoveByAnimationInfoAsync(Guid animationInfoId, CancellationToken cancellationToken)
        => throw new NotSupportedException();

    private static string ParentPath(string path)
    {
        var slash = path.LastIndexOf('/');
        return slash <= 0 ? "/" : path[..slash];
    }
}

internal sealed class FakeFileExplorer : IFileExplorer
{
    private readonly List<FileMapping> _mappings;
    private readonly IFileStore _store;
    private readonly FakeFileMappingRepository _repository;

    public FakeFileExplorer(List<FileMapping> mappings, IFileStore store, FakeFileMappingRepository repository)
    {
        _mappings = mappings;
        _store = store;
        _repository = repository;
    }

    private List<FileMapping> Snapshot() => _mappings.ToList();

    public async Task<IReadOnlyList<IFileExploreToken>> EnumerateDirectoryAsync(DirectoryToken token, CancellationToken cancellationToken)
    {
        var prefix = token.Path.EndsWith('/') ? token.Path : token.Path + "/";

        if (prefix == "/")
        {
            var roots = await _repository.GetRootEntriesAsync(cancellationToken);
            return roots
                .Select<RootEntry, IFileExploreToken>(r => r.IsDirectory
                    ? new DirectoryToken("/" + r.Name, r.Name)
                    : new FileToken("/" + r.Name, r.Name))
                .ToList();
        }

        var direct = new Dictionary<string, IFileExploreToken>(StringComparer.Ordinal);

        foreach (var m in Snapshot())
        {
            if (!m.VirtualPath.StartsWith(prefix, StringComparison.Ordinal)) continue;
            var rel = m.VirtualPath[prefix.Length..];
            if (rel.Length == 0) continue;

            var slash = rel.IndexOf('/');
            if (slash < 0)
            {
                direct[rel] = new FileToken(m.VirtualPath, rel);
            }
            else
            {
                var name = rel[..slash];
                if (!direct.ContainsKey(name))
                    direct[name] = new DirectoryToken(prefix + name, name);
            }
        }

        return direct.Values.ToList();
    }

    public async Task<IReadOnlyList<FileExploreEntry>> GetDirectoryEntriesAsync(
        DirectoryToken token,
        CancellationToken cancellationToken)
    {
        var nodes = await _repository.GetImmediateChildrenAsync(
            token.Path == "/" ? "/" : token.Path.TrimEnd('/'),
            cancellationToken);
        var results = new List<FileExploreEntry>(nodes.Count);
        foreach (var node in nodes)
        {
            FileStoreInfo? info = null;
            if (node.Mapping is not null)
                info = await _store.FileInfoAsync(node.Mapping.PhysicalPath, cancellationToken);
            results.Add(new FileExploreEntry(
                node.Path,
                node.Name,
                node.IsDirectory,
                node.Mapping,
                info));
        }

        return results;
    }

    public Task<Stream> OpenReadStreamAsync(FileToken token, CancellationToken cancellationToken)
    {
        var mapping = Snapshot().FirstOrDefault(m => m.VirtualPath == token.Path)
                      ?? throw new FileNotFoundException(token.Path);
        return _store.OpenReadStreamAsync(mapping.PhysicalPath, cancellationToken);
    }
}
